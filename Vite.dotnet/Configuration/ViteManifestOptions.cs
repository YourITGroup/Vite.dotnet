namespace Vite.Configuration;

/// <summary>
/// Configuration for <see cref="IViteManifestService"/>, including the defaults
/// used by the parameterless asset getters.
/// </summary>
public sealed class ViteManifestOptions
{
  /// <summary>
  /// The appsettings configuration section these options bind from.
  /// </summary>
  public const string SectionName = "ViteManifest";

  /// <summary>
  /// The logical entry assumed when none is supplied (e.g. "index.html").
  /// A leading "~/" is tolerated. Leave unset to have the entry discovered from the
  /// manifest: the single <c>isEntry</c> record with an ".html" key, or failing that
  /// an ".js"-family key.
  /// </summary>
  public string? DefaultEntry { get; set; }

  /// <summary>
  /// The base path the hashed assets are served from when none is supplied (e.g. "/").
  /// </summary>
  public string DefaultBasePath { get; set; } = "/";

  /// <summary>
  /// The Vite dev server to render assets from when none is supplied, as either
  /// <c>host:port</c> ("localhost:5173" — assumed http) or a full origin
  /// ("https://localhost:5173"). Only honoured in the Development environment, so it is
  /// safe to leave configured; set it in appsettings.Development.json to get HMR without
  /// a <c>dev-server</c> attribute on every tag.
  /// </summary>
  public string? DevServer { get; set; }
}
