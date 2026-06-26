namespace Vite.Models;

/// <summary>
/// Which asset tags to render for a Vite entry.
/// </summary>
[Flags]
public enum ViteAssets
{
  /// <summary>Render nothing.</summary>
  None = 0,

  /// <summary>Render the CSS &lt;link&gt; tags.</summary>
  Css = 1,

  /// <summary>Render the module &lt;script&gt; tag.</summary>
  Js = 2,

  /// <summary>Render both CSS and JS (the default).</summary>
  All = Css | Js,
}
