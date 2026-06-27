using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Html;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using Vite.Configuration;
using Vite.Models;

namespace Vite.Services;

public sealed class ViteManifestService : IViteManifestService
{
  private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

  private readonly IWebHostEnvironment _env;
  private readonly ILogger<ViteManifestService> _logger;
  private readonly ViteManifestOptions _options;
  private readonly string _manifestPath;

  // Lazy + default thread-safety mode => the manifest file is read and parsed
  // exactly once, no matter how many requests hit the tag helper concurrently.
  private readonly Lazy<IReadOnlyDictionary<string, ViteManifestEntry>> _manifest;

  public ViteManifestService(IWebHostEnvironment env, ILogger<ViteManifestService> logger, IOptions<ViteManifestOptions> options)
  {
    _env = env;
    _logger = logger;
    _options = options.Value;
    _manifestPath = Path.Combine(env.WebRootPath, ".vite", "manifest.json");
    _manifest = new Lazy<IReadOnlyDictionary<string, ViteManifestEntry>>(LoadManifest);
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
    if (entry.Css is null || entry.Css.Count == 0)
    {
      return [];
    }

    var basePrefix = basePath.TrimEnd('/');
    return [.. entry.Css.Select(css => $"{basePrefix}/{css}")];
  }

  public string? GetJsFile()
    => GetJsFile(_options.DefaultEntry, _options.DefaultBasePath);

  public string? GetJsFile(string entry, string basePath)
    => GetEntry(entry) is { } asset ? GetJsFile(asset, basePath) : null;

  public string? GetJsFile(ViteManifestEntry entry, string basePath)
    => string.IsNullOrEmpty(entry.File) ? null : $"{basePath.TrimEnd('/')}/{entry.File}";

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
    return src is null ? HtmlString.Empty : new HtmlString($"<script type=\"module\" src=\"{src}\"></script>\n");
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
