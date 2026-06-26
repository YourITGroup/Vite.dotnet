using Microsoft.AspNetCore.Razor.TagHelpers;
using Vite.Models;
using Vite.Services;

namespace Vite;

[HtmlTargetElement("vite")]
public class ViteTagHelper(IViteManifestService manifest) : TagHelper
{
  /// <summary>
  /// Logical entry name from Vite (e.g., "src/main.js" or "index.html").
  /// When omitted, the configured <c>DefaultEntry</c> is used.
  /// </summary>
  [HtmlAttributeName("entry")]
  public string? Entry { get; set; }

  /// <summary>
  /// Base path for production assets. When omitted, the configured <c>DefaultBasePath</c> is used.
  /// </summary>
  [HtmlAttributeName("base-path")]
  public string? BasePath { get; set; }

  /// <summary>
  /// If true, output <link rel="preload" as="style"> for CSS before normal stylesheet links
  /// </summary>
  [HtmlAttributeName("preload-css")]
  public bool PreloadCss { get; set; } = false;

  /// <summary>
  /// Optional dev server url and port
  /// </summary>
  [HtmlAttributeName("dev-server")]
  public string DevServer { get; set; } = "";

  /// <summary>
  /// Which asset tags to render: "Css", "Js" or "All" (default).
  /// </summary>
  [HtmlAttributeName("assets")]
  public ViteAssets Assets { get; set; } = ViteAssets.All;

  public override void Process(TagHelperContext context, TagHelperOutput output)
  {
    output.TagName = null; // Remove <vite> wrapper
    output.Content.SetHtmlContent(manifest.RenderEntry(Entry, BasePath, PreloadCss, Assets, DevServer));
  }
}
