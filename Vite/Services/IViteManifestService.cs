using Microsoft.AspNetCore.Html;
using Vite.Models;

namespace Vite.Services;

/// <summary>
/// Resolves Vite entries to their hashed assets and renders the corresponding
/// &lt;link&gt;/&lt;script&gt; markup. The manifest is read and parsed once, then
/// cached for the lifetime of the application (it is a build artifact that does
/// not change at runtime), so it is not re-read on every page render.
/// </summary>
public interface IViteManifestService
{
  /// <summary>
  /// Resolves a logical entry from the cached manifest. A leading "~/" is tolerated.
  /// Returns <c>null</c> when the entry is not present in the manifest.
  /// </summary>
  ViteManifestEntry? GetEntry(string entry);

  /// <summary>
  /// Returns the base-path-resolved CSS file URLs for the configured default entry and base path
  /// (see <see cref="ViteManifestOptions"/>).
  /// </summary>
  IReadOnlyList<string> GetCssFiles();

  /// <summary>
  /// Returns the base-path-resolved CSS file URLs for a logical entry (e.g. "/css/index.abc123.css"),
  /// so other services can reference them. Empty when the entry is missing or has no CSS.
  /// </summary>
  IReadOnlyList<string> GetCssFiles(string entry, string basePath);

  /// <summary>
  /// Returns the base-path-resolved CSS file URLs for a manifest entry.
  /// </summary>
  IReadOnlyList<string> GetCssFiles(ViteManifestEntry entry, string basePath);

  /// <summary>
  /// Returns the base-path-resolved JS file URL for the configured default entry and base path
  /// (see <see cref="ViteManifestOptions"/>).
  /// </summary>
  string? GetJsFile();

  /// <summary>
  /// Returns the base-path-resolved JS file URL for a logical entry (e.g. "/js/index.abc123.js"),
  /// so other services can reference it. <c>null</c> when the entry is missing or has no JS file.
  /// </summary>
  string? GetJsFile(string entry, string basePath);

  /// <summary>
  /// Returns the base-path-resolved JS file URL for a manifest entry.
  /// </summary>
  string? GetJsFile(ViteManifestEntry entry, string basePath);

  /// <summary>
  /// Renders only the CSS &lt;link&gt; tags for a manifest entry.
  /// </summary>
  /// <param name="entry">The manifest entry (see <see cref="GetEntry"/>).</param>
  /// <param name="basePath">Base path the hashed assets are served from (e.g. "/").</param>
  /// <param name="preload">When true, emit preload links with a &lt;noscript&gt; fallback instead of plain stylesheet links.</param>
  IHtmlContent RenderCss(ViteManifestEntry entry, string basePath, bool preload = false);

  /// <summary>
  /// Renders only the module &lt;script&gt; tag for a manifest entry.
  /// </summary>
  /// <param name="entry">The manifest entry (see <see cref="GetEntry"/>).</param>
  /// <param name="basePath">Base path the hashed assets are served from (e.g. "/").</param>
  IHtmlContent RenderJs(ViteManifestEntry entry, string basePath);

  /// <summary>
  /// Renders the requested asset tags for a Vite entry. Handles the dev-server
  /// short-circuit and the "entry not found" case, then composes the CSS and/or
  /// JS tags according to <paramref name="assets"/>.
  /// </summary>
  /// <param name="entry">Logical entry name (e.g. "index.html"). A leading "~/" is tolerated. Falls back to <see cref="ViteManifestOptions.DefaultEntry"/> when null/empty.</param>
  /// <param name="basePath">Base path the hashed assets are served from (e.g. "/"). Falls back to <see cref="ViteManifestOptions.DefaultBasePath"/> when null/empty.</param>
  /// <param name="preloadCss">When true, emit preload links for CSS with a &lt;noscript&gt; fallback.</param>
  /// <param name="assets">Which asset tags to render (CSS, JS, or both). Defaults to both.</param>
  /// <param name="devServer">Optional Vite dev-server host:port; only used in the Development environment.</param>
  IHtmlContent RenderEntry(string? entry = null, string? basePath = null, bool preloadCss = false, ViteAssets assets = ViteAssets.All, string? devServer = null);
}
