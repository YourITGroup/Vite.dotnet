namespace Vite.Models;

/// <summary>
/// A single entry in a Vite build manifest (manifest.json).
/// </summary>
public sealed class ViteManifestEntry
{
  /// <summary>The hashed output file for this entry (e.g. "js/index.2A98uGZn.js").</summary>
  public string File { get; set; } = "";

  /// <summary>The hashed CSS files associated with this entry, if any.</summary>
  public List<string>? Css { get; set; }

  /// <summary>The logical names of other entries this entry imports, if any.</summary>
  public List<string>? Imports { get; set; }

  /// <summary>
  /// True when this record is one of the build's configured entry points rather than a
  /// shared/split chunk. Used to auto-discover the default entry when none is configured.
  /// </summary>
  public bool IsEntry { get; set; }
}
