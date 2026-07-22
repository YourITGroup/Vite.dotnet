using Microsoft.AspNetCore.Http;
using Vite;
using Vite.Configuration;
using Xunit;

namespace Vite.dotnet.Tests;

public class UnhashedAssetRedirectTests
{
  private const string Manifest = """
  {
    "_client.gen-WrdE9yCN.js": {
      "file": "assets/client.gen-WrdE9yCN.js",
      "name": "client.gen",
      "css": ["assets/client-CYzq3z7c.css"]
    },
    "index.html": {
      "file": "assets/main-espf9ZVg.js",
      "name": "main",
      "isEntry": true,
      "imports": ["_client.gen-WrdE9yCN.js"],
      "css": ["assets/main-COZv9l4K.css"]
    }
  }
  """;

  private static ViteManifestOptions WithBasePath(string basePath) =>
    new() { DefaultBasePath = basePath };

  [Fact]
  public void TryResolveHashedAsset_MapsUnhashedJsEntryToHashedFile()
  {
    using var ctx = ManifestTestContext.Create(Manifest);

    Assert.True(ctx.Service.TryResolveHashedAsset("/assets/main.js", out var hashed));
    Assert.Equal("/assets/main-espf9ZVg.js", hashed);
  }

  [Fact]
  public void TryResolveHashedAsset_MapsUnhashedCssToHashedFile()
  {
    using var ctx = ManifestTestContext.Create(Manifest);

    Assert.True(ctx.Service.TryResolveHashedAsset("/assets/main.css", out var hashed));
    Assert.Equal("/assets/main-COZv9l4K.css", hashed);
  }

  [Fact]
  public void TryResolveHashedAsset_MapsCssFromImportedChunk()
  {
    using var ctx = ManifestTestContext.Create(Manifest);

    Assert.True(ctx.Service.TryResolveHashedAsset("/assets/client.css", out var hashed));
    Assert.Equal("/assets/client-CYzq3z7c.css", hashed);
  }

  [Fact]
  public void TryResolveHashedAsset_KeepsNamesContainingDots()
  {
    using var ctx = ManifestTestContext.Create(Manifest);

    Assert.True(ctx.Service.TryResolveHashedAsset("/assets/client.gen.js", out var hashed));
    Assert.Equal("/assets/client.gen-WrdE9yCN.js", hashed);
  }

  [Fact]
  public void TryResolveHashedAsset_AppliesConfiguredBasePath()
  {
    using var ctx = ManifestTestContext.Create(Manifest, WithBasePath("/dist/"));

    Assert.True(ctx.Service.TryResolveHashedAsset("/dist/assets/main.css", out var hashed));
    Assert.Equal("/dist/assets/main-COZv9l4K.css", hashed);
  }

  [Fact]
  public void TryResolveHashedAsset_ReturnsFalseForUnknownPath()
  {
    using var ctx = ManifestTestContext.Create(Manifest);

    Assert.False(ctx.Service.TryResolveHashedAsset("/assets/nope.css", out _));
  }

  [Fact]
  public void TryResolveHashedAsset_DoesNotMatchTheHashedPathItself()
  {
    using var ctx = ManifestTestContext.Create(Manifest);

    // The already-hashed file is served by static files, not redirected.
    Assert.False(ctx.Service.TryResolveHashedAsset("/assets/main-COZv9l4K.css", out _));
  }

  [Fact]
  public void BuildMap_SkipsAndWarnsOnAmbiguousCollision()
  {
    // Two hashed files in the same folder that dehash to the same unhashed name.
    const string manifest = """
    {
      "a.html": { "file": "assets/app-AAAAAAAA.js", "isEntry": true },
      "b.html": { "file": "assets/app-BBBBBBBB.js", "isEntry": true }
    }
    """;
    using var ctx = ManifestTestContext.Create(manifest);

    Assert.True(ctx.Service.TryResolveHashedAsset("/assets/app.js", out var hashed));
    // First-seen wins; exactly one of the two, never both.
    Assert.Contains(hashed, new[] { "/assets/app-AAAAAAAA.js", "/assets/app-BBBBBBBB.js" });
    Assert.Contains(ctx.Warnings, w => w.Contains("Ambiguous unhashed asset"));
  }

  [Fact]
  public void BuildMap_IgnoresFilesWithoutAHashSegment()
  {
    const string manifest = """
    { "index.html": { "file": "assets/plain.js", "isEntry": true, "css": ["assets/plain.css"] } }
    """;
    using var ctx = ManifestTestContext.Create(manifest);

    Assert.False(ctx.Service.TryResolveHashedAsset("/assets/plain.js", out _));
    Assert.False(ctx.Service.TryResolveHashedAsset("/assets/plain.css", out _));
  }

  [Fact]
  public async Task Middleware_Redirects302ToHashedFile()
  {
    using var ctx = ManifestTestContext.Create(Manifest);
    var context = new DefaultHttpContext();
    context.Request.Method = HttpMethods.Get;
    context.Request.Path = "/assets/main.css";
    var nextCalled = false;

    var middleware = new ViteUnhashedAssetRedirectMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, ctx.Service);
    await middleware.InvokeAsync(context);

    Assert.False(nextCalled);
    Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
    Assert.Equal("/assets/main-COZv9l4K.css", context.Response.Headers.Location);
  }

  [Fact]
  public async Task Middleware_PreservesQueryString()
  {
    using var ctx = ManifestTestContext.Create(Manifest);
    var context = new DefaultHttpContext();
    context.Request.Method = HttpMethods.Get;
    context.Request.Path = "/assets/main.css";
    context.Request.QueryString = new QueryString("?v=1");

    var middleware = new ViteUnhashedAssetRedirectMiddleware(_ => Task.CompletedTask, ctx.Service);
    await middleware.InvokeAsync(context);

    Assert.Equal("/assets/main-COZv9l4K.css?v=1", context.Response.Headers.Location);
  }

  [Fact]
  public async Task Middleware_PassesThroughUnknownPath()
  {
    using var ctx = ManifestTestContext.Create(Manifest);
    var context = new DefaultHttpContext();
    context.Request.Method = HttpMethods.Get;
    context.Request.Path = "/assets/unknown.css";
    var nextCalled = false;

    var middleware = new ViteUnhashedAssetRedirectMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, ctx.Service);
    await middleware.InvokeAsync(context);

    Assert.True(nextCalled);
    Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
  }

  [Fact]
  public async Task Middleware_PassesThroughNonGetRequests()
  {
    using var ctx = ManifestTestContext.Create(Manifest);
    var context = new DefaultHttpContext();
    context.Request.Method = HttpMethods.Post;
    context.Request.Path = "/assets/main.css";
    var nextCalled = false;

    var middleware = new ViteUnhashedAssetRedirectMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, ctx.Service);
    await middleware.InvokeAsync(context);

    Assert.True(nextCalled);
  }
}
