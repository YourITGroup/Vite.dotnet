using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vite.Configuration;
using Vite.Services;

namespace Vite.dotnet.Tests;

/// <summary>
/// Builds a <see cref="ViteManifestService"/> backed by a real manifest.json written
/// into a throwaway web-root directory, then cleans the directory up on dispose.
/// The service reads <c>{WebRootPath}/.vite/manifest.json</c> once and caches it, so
/// each test gets its own isolated context.
/// </summary>
public sealed class ManifestTestContext : IDisposable
{
  private readonly string _webRoot;

  private ManifestTestContext(string webRoot, ViteManifestService service)
  {
    _webRoot = webRoot;
    Service = service;
  }

  public ViteManifestService Service { get; }

  public static ManifestTestContext Create(string manifestJson, ViteManifestOptions? options = null)
  {
    var webRoot = Path.Combine(Path.GetTempPath(), "vite-dotnet-tests", Guid.NewGuid().ToString("N"));
    var viteDir = Path.Combine(webRoot, ".vite");
    Directory.CreateDirectory(viteDir);
    File.WriteAllText(Path.Combine(viteDir, "manifest.json"), manifestJson);

    var env = new FakeWebHostEnvironment { WebRootPath = webRoot };
    var service = new ViteManifestService(
      env,
      NullLogger<ViteManifestService>.Instance,
      Options.Create(options ?? new ViteManifestOptions()));

    return new ManifestTestContext(webRoot, service);
  }

  /// <summary>Creates a context whose manifest file does not exist on disk.</summary>
  public static ManifestTestContext CreateWithoutManifest(ViteManifestOptions? options = null)
  {
    var webRoot = Path.Combine(Path.GetTempPath(), "vite-dotnet-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(webRoot);

    var env = new FakeWebHostEnvironment { WebRootPath = webRoot };
    var service = new ViteManifestService(
      env,
      NullLogger<ViteManifestService>.Instance,
      Options.Create(options ?? new ViteManifestOptions()));

    return new ManifestTestContext(webRoot, service);
  }

  public void Dispose()
  {
    try
    {
      if (Directory.Exists(_webRoot))
      {
        Directory.Delete(_webRoot, recursive: true);
      }
    }
    catch (IOException)
    {
      // Best-effort cleanup; a locked temp file must not fail the test run.
    }
  }

  private sealed class FakeWebHostEnvironment : IWebHostEnvironment
  {
    public string WebRootPath { get; set; } = "";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ApplicationName { get; set; } = "Vite.dotnet.Tests";
    public string ContentRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string EnvironmentName { get; set; } = "Production";
  }
}
