using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Html;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vite.Configuration;
using Vite.Models;

namespace Vite.Services;

public sealed partial class ViteManifestService : IViteManifestService
{
  private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

  // Vite's default output filename is "[name]-[hash].[ext]". The hash is the trailing
  // "-" group; requiring >=8 chars from Vite's default hash alphabet avoids stripping a
  // real "-" word (e.g. "vue-tel-input") that happens to sit before the extension.
  [GeneratedRegex(@"^(?<name>.+)-[A-Za-z0-9_-]{8,}(?<ext>\.[^.]+)$")]
  private static partial Regex HashedFileRegex();

  private readonly IWebHostEnvironment _env;
  private readonly ILogger<ViteManifestService> _logger;
  private readonly ViteManifestOptions _options;
  private readonly string _manifestPath;

  // Lazy + default thread-safety mode => the manifest file is read and parsed
  // exactly once, no matter how many requests hit the tag helper concurrently.
  private readonly Lazy<IReadOnlyDictionary<string, ViteManifestEntry>> _manifest;

  // Unhashed URL path -> hashed URL path, derived once from the manifest and prefixed
  // with the configured base path. Populated only when RedirectUnhashedAssets is enabled.
  private readonly Lazy<IReadOnlyDictionary<string, string>> _unhashedAssetMap;

  public ViteManifestService(IWebHostEnvironment env, ILogger<ViteManifestService> logger, IOptions<ViteManifestOptions> options)
  {
    _env = env;
    _logger = logger;
    _options = options.Value;
    _manifestPath = Path.Combine(env.WebRootPath, ".vite", "manifest.json");
    _manifest = new Lazy<IReadOnlyDictionary<string, ViteManifestEntry>>(LoadManifest);
    _unhashedAssetMap = new Lazy<IReadOnlyDictionary<string, string>>(BuildUnhashedAssetMap);
  }

  public ViteManifestEntry? GetEntry(string entry)
  {
    if (string.IsNullOrWhiteSpace(entry))
    {
      return null;
    }

    // Razor authors write "~/index.html", but the manifest is keyed by the
    // logical entry name (e.g. "index.html"). Strip the "~/" so the lookup matches.
    var entryKey = entry.TrimStart('~').TrimStart('/');

    return _manifest.Value.TryGetValue(entryKey, out var asset) ? asset : null;
  }

  public IReadOnlyList<string> GetCssFiles()
    => GetCssFiles(_options.DefaultEntry, _options.DefaultBasePath);

  public IReadOnlyList<string> GetCssFiles(string entry, string basePath)
    => GetEntry(entry) is { } asset ? GetCssFiles(asset, basePath) : [];

  public IReadOnlyList<string> GetCssFiles(ViteManifestEntry entry, string basePath)
  {
    // Collect CSS from the whole dependency graph: an entry's own `css` only
    // covers the modules bundled into its chunk. CSS pulled in by shared/imported
    // chunks (Vite splits those out) lives on those chunks' manifest records and
    // is reachable only by walking `imports` transitively.
    var collected = new List<string>();
    var seenCss = new HashSet<string>(StringComparer.Ordinal);
    var visited = new HashSet<string>(StringComparer.Ordinal);
    CollectCss(entry, collected, seenCss, visited);

    if (collected.Count == 0)
    {
      return [];
    }

    var basePrefix = basePath.TrimEnd('/');
    return [.. collected.Select(css => $"{basePrefix}/{css}")];
  }

  // Depth-first walk of the import graph. Imported chunks are processed before the
  // current entry's own CSS so a dependency's styles load first and the entry's own
  // styles can override them (mirrors Vite's own backend-integration collectCss).
  private void CollectCss(ViteManifestEntry entry, List<string> collected, HashSet<string> seenCss, HashSet<string> visited)
  {
    if (entry.Imports is { Count: > 0 })
    {
      foreach (var import in entry.Imports)
      {
        // Guard against cycles and redundant work when several entries share a chunk.
        if (!visited.Add(import))
        {
          continue;
        }

        if (_manifest.Value.TryGetValue(import, out var importedChunk))
        {
          CollectCss(importedChunk, collected, seenCss, visited);
        }
      }
    }

    if (entry.Css is { Count: > 0 })
    {
      foreach (var css in entry.Css)
      {
        if (seenCss.Add(css))
        {
          collected.Add(css);
        }
      }
    }
  }

  public string? GetJsFile()
    => GetJsFile(_options.DefaultEntry, _options.DefaultBasePath);

  public string? GetJsFile(string entry, string basePath)
    => GetEntry(entry) is { } asset ? GetJsFile(asset, basePath) : null;

  public string? GetJsFile(ViteManifestEntry entry, string basePath)
    => string.IsNullOrEmpty(entry.File) ? null : $"{basePath.TrimEnd('/')}/{entry.File}";

  public IReadOnlyList<string> GetModulePreloadFiles(ViteManifestEntry entry, string basePath)
  {
    // The browser's ES module loader fetches statically-imported chunks on its own,
    // but only after it has parsed the entry chunk. Emitting modulepreload hints for
    // those imported chunks removes that discovery round-trip (matches Vite's own
    // generated HTML). Walks the same import graph GetCssFiles uses.
    var collected = new List<string>();
    var seenFiles = new HashSet<string>(StringComparer.Ordinal);
    var visited = new HashSet<string>(StringComparer.Ordinal);
    CollectImportedJs(entry, collected, seenFiles, visited);

    if (collected.Count == 0)
    {
      return [];
    }

    var basePrefix = basePath.TrimEnd('/');
    return [.. collected.Select(file => $"{basePrefix}/{file}")];
  }

  // Depth-first walk collecting the output file of every transitively imported chunk
  // (deepest dependency first). The entry's own file is excluded — it gets a real
  // <script> tag, not a preload.
  private void CollectImportedJs(ViteManifestEntry entry, List<string> collected, HashSet<string> seenFiles, HashSet<string> visited)
  {
    if (entry.Imports is not { Count: > 0 })
    {
      return;
    }

    foreach (var import in entry.Imports)
    {
      // Guard against cycles and redundant work when several entries share a chunk.
      if (!visited.Add(import))
      {
        continue;
      }

      if (_manifest.Value.TryGetValue(import, out var importedChunk))
      {
        CollectImportedJs(importedChunk, collected, seenFiles, visited);

        if (!string.IsNullOrEmpty(importedChunk.File) && seenFiles.Add(importedChunk.File))
        {
          collected.Add(importedChunk.File);
        }
      }
    }
  }

  public IHtmlContent RenderCss(ViteManifestEntry entry, string basePath, bool preload = false)
  {
    var hrefs = GetCssFiles(entry, basePath);
    if (hrefs.Count == 0)
    {
      return HtmlString.Empty;
    }

    var html = new StringBuilder();

    if (preload)
    {
      foreach (var href in hrefs)
      {
        html.AppendLine($"<link rel=\"preload\" as=\"style\" href=\"{href}\" onload=\"this.onload=null;this.rel='stylesheet'\"/>");
      }
      html.AppendLine("<noscript>");
      foreach (var href in hrefs)
      {
        html.AppendLine($"<link rel=\"stylesheet\" href=\"{href}\" />");
      }
      html.AppendLine("</noscript>");
    }
    else
    {
      foreach (var href in hrefs)
      {
        html.AppendLine($"<link rel=\"stylesheet\" href=\"{href}\" />");
      }
    }

    return new HtmlString(html.ToString());
  }

  public IHtmlContent RenderJs(ViteManifestEntry entry, string basePath)
  {
    var src = GetJsFile(entry, basePath);
    if (src is null)
    {
      return HtmlString.Empty;
    }

    var html = new StringBuilder();

    // Preload statically-imported chunks so the browser fetches them in parallel with
    // the entry rather than discovering them only after the entry has been parsed.
    foreach (var preload in GetModulePreloadFiles(entry, basePath))
    {
      html.AppendLine($"<link rel=\"modulepreload\" href=\"{preload}\" />");
    }

    html.Append($"<script type=\"module\" src=\"{src}\"></script>\n");
    return new HtmlString(html.ToString());
  }

  public IHtmlContent RenderEntry(string? entry = null, string? basePath = null, bool preloadCss = false, ViteAssets assets = ViteAssets.All, string? devServer = null)
  {
    // Fall back to the configured defaults when not supplied.
    entry = string.IsNullOrWhiteSpace(entry) ? _options.DefaultEntry : entry;
    basePath = string.IsNullOrWhiteSpace(basePath) ? _options.DefaultBasePath : basePath;

    if (_env.IsDevelopment() && !string.IsNullOrWhiteSpace(devServer))
    {
      // Dev mode: point straight at the Vite dev server (no manifest involved).
      // The dev server injects CSS via the module script, so a single tag covers both.
      var entryKey = entry.TrimStart('~').TrimStart('/');
      return new HtmlString($"<script type=\"module\" src=\"http://{devServer}/{entryKey}\"></script>");
    }

    var manifestEntry = GetEntry(entry);
    if (manifestEntry is null)
    {
      return new HtmlString($"<!-- Vite entry '{entry}' not found in manifest -->");
    }

    var content = new HtmlContentBuilder();

    if (assets.HasFlag(ViteAssets.Css))
    {
      content.AppendHtml(RenderCss(manifestEntry, basePath, preloadCss));
    }

    if (assets.HasFlag(ViteAssets.Js))
    {
      content.AppendHtml(RenderJs(manifestEntry, basePath));
    }

    return content;
  }

  public bool TryResolveHashedAsset(string requestPath, out string hashedPath)
  {
    hashedPath = "";
    if (string.IsNullOrEmpty(requestPath))
    {
      return false;
    }

    if (_unhashedAssetMap.Value.TryGetValue(requestPath, out var resolved))
    {
      hashedPath = resolved;
      return true;
    }

    return false;
  }

  // Builds the unhashed -> hashed lookup from every output file the manifest references
  // (entry/chunk JS files and all CSS files). Keys and values are base-path-prefixed URL
  // paths so the middleware can compare against the incoming request path directly.
  // Built lazily, so the cost is only paid when the redirect middleware is registered.
  private IReadOnlyDictionary<string, string> BuildUnhashedAssetMap()
  {
    var basePrefix = _options.DefaultBasePath.TrimEnd('/');
    var map = new Dictionary<string, string>(StringComparer.Ordinal);

    void Add(string? file)
    {
      if (string.IsNullOrEmpty(file))
      {
        return;
      }

      var match = HashedFileRegex().Match(file);
      if (!match.Success)
      {
        return; // No hash segment -> nothing to redirect from.
      }

      var unhashed = match.Groups["name"].Value + match.Groups["ext"].Value;
      var unhashedUrl = $"{basePrefix}/{unhashed}";
      var hashedUrl = $"{basePrefix}/{file}";

      if (map.TryGetValue(unhashedUrl, out var existing))
      {
        if (!string.Equals(existing, hashedUrl, StringComparison.Ordinal))
        {
          _logger.LogWarning(
            "Ambiguous unhashed asset '{Unhashed}' maps to both '{Existing}' and '{New}'; keeping the first and skipping the redirect for the second.",
            unhashedUrl, existing, hashedUrl);
        }
        return; // First mapping wins; never emit an ambiguous redirect.
      }

      map[unhashedUrl] = hashedUrl;
    }

    foreach (var entry in _manifest.Value.Values)
    {
      Add(entry.File);

      if (entry.Css is { Count: > 0 })
      {
        foreach (var css in entry.Css)
        {
          Add(css);
        }
      }
    }

    return map;
  }

  private IReadOnlyDictionary<string, ViteManifestEntry> LoadManifest()
  {
    if (File.Exists(_manifestPath))
    {
      var json = File.ReadAllText(_manifestPath);
      var manifest = JsonSerializer.Deserialize<Dictionary<string, ViteManifestEntry>>(json, JsonOptions);
      if (manifest != null)
      {
        if (_logger.IsEnabled(LogLevel.Information))
        {
          _logger.LogInformation("Loaded Vite manifest found at {ManifestPath}", _manifestPath);
        }
        return manifest;
      }
    }

    _logger.LogWarning("Could not find vite manifest at {ManifestPath} - has it been included in the build output?", _manifestPath);
    return new Dictionary<string, ViteManifestEntry>();
  }
}
