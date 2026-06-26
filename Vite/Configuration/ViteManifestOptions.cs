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
  /// A leading "~/" is tolerated.
  /// </summary>
  public string DefaultEntry { get; set; } = "index.html";

  /// <summary>
  /// The base path the hashed assets are served from when none is supplied (e.g. "/").
  /// </summary>
  public string DefaultBasePath { get; set; } = "/";
}
