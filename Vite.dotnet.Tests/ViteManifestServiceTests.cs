using Vite.Configuration;
using Vite.Models;
using Xunit;

namespace Vite.dotnet.Tests;

public class ViteManifestServiceTests
{
  // A manifest shaped like a real multi-entry Vite build: two entries share a split
  // chunk ("_client.gen") that carries its own CSS, exactly the case the transitive
  // walk exists to cover.
  private const string SharedChunkManifest = """
  {
    "_client.gen-WrdE9yCN.js": {
      "file": "assets/client.gen-WrdE9yCN.js",
      "name": "client.gen",
      "css": ["assets/client-CYzq3z7c.css"]
    },
    "index.html": {
      "file": "assets/main-espf9ZVg.js",
      "name": "main",
      "src": "index.html",
      "isEntry": true,
      "imports": ["_client.gen-WrdE9yCN.js"],
      "css": ["assets/main-COZv9l4K.css"]
    },
    "checkout/index.html": {
      "file": "assets/checkout-DLgJ9jU3.js",
      "name": "checkout",
      "src": "checkout/index.html",
      "isEntry": true,
      "imports": ["_client.gen-WrdE9yCN.js"],
      "css": ["assets/checkout-CfozXp2E.css"]
    }
  }
  """;

  [Fact]
  public void GetCssFiles_IncludesCssFromImportedChunk()
  {
    using var ctx = ManifestTestContext.Create(SharedChunkManifest);

    var css = ctx.Service.GetCssFiles("index.html", "/");

    Assert.Contains("/assets/client-CYzq3z7c.css", css);
    Assert.Contains("/assets/main-COZv9l4K.css", css);
  }

  [Fact]
  public void GetCssFiles_OrdersImportedCssBeforeOwnCss()
  {
    using var ctx = ManifestTestContext.Create(SharedChunkManifest);

    var css = ctx.Service.GetCssFiles("checkout/index.html", "/");

    Assert.Equal(
      ["/assets/client-CYzq3z7c.css", "/assets/checkout-CfozXp2E.css"],
      css);
  }

  [Fact]
  public void GetCssFiles_AppliesBasePathToImportedCss()
  {
    using var ctx = ManifestTestContext.Create(SharedChunkManifest);

    var css = ctx.Service.GetCssFiles("index.html", "/dist/");

    Assert.Equal(
      ["/dist/assets/client-CYzq3z7c.css", "/dist/assets/main-COZv9l4K.css"],
      css);
  }

  [Fact]
  public void GetCssFiles_DeduplicatesCssSharedAcrossImports()
  {
    // Two imported chunks that both reference the same CSS file.
    const string manifest = """
    {
      "_a.js": { "file": "assets/a.js", "css": ["assets/shared.css"] },
      "_b.js": { "file": "assets/b.js", "css": ["assets/shared.css"] },
      "index.html": {
        "file": "assets/main.js",
        "isEntry": true,
        "imports": ["_a.js", "_b.js"],
        "css": ["assets/main.css"]
      }
    }
    """;
    using var ctx = ManifestTestContext.Create(manifest);

    var css = ctx.Service.GetCssFiles("index.html", "/");

    Assert.Equal(
      ["/assets/shared.css", "/assets/main.css"],
      css);
  }

  [Fact]
  public void GetCssFiles_WalksNestedImportsTransitively()
  {
    // entry -> _mid -> _leaf, each contributing CSS.
    const string manifest = """
    {
      "_leaf.js": { "file": "assets/leaf.js", "css": ["assets/leaf.css"] },
      "_mid.js": { "file": "assets/mid.js", "imports": ["_leaf.js"], "css": ["assets/mid.css"] },
      "index.html": {
        "file": "assets/main.js",
        "isEntry": true,
        "imports": ["_mid.js"],
        "css": ["assets/main.css"]
      }
    }
    """;
    using var ctx = ManifestTestContext.Create(manifest);

    var css = ctx.Service.GetCssFiles("index.html", "/");

    Assert.Equal(
      ["/assets/leaf.css", "/assets/mid.css", "/assets/main.css"],
      css);
  }

  [Fact]
  public void GetCssFiles_DoesNotInfiniteLoopOnCyclicImports()
  {
    // _a imports _b, _b imports _a. The cycle guard must terminate the walk.
    const string manifest = """
    {
      "_a.js": { "file": "assets/a.js", "imports": ["_b.js"], "css": ["assets/a.css"] },
      "_b.js": { "file": "assets/b.js", "imports": ["_a.js"], "css": ["assets/b.css"] },
      "index.html": {
        "file": "assets/main.js",
        "isEntry": true,
        "imports": ["_a.js"],
        "css": ["assets/main.css"]
      }
    }
    """;
    using var ctx = ManifestTestContext.Create(manifest);

    var css = ctx.Service.GetCssFiles("index.html", "/");

    Assert.Equal(3, css.Count);
    Assert.Contains("/assets/a.css", css);
    Assert.Contains("/assets/b.css", css);
    Assert.Contains("/assets/main.css", css);
  }

  [Fact]
  public void GetCssFiles_IgnoresUnknownImportKeys()
  {
    const string manifest = """
    {
      "index.html": {
        "file": "assets/main.js",
        "isEntry": true,
        "imports": ["_missing.js"],
        "css": ["assets/main.css"]
      }
    }
    """;
    using var ctx = ManifestTestContext.Create(manifest);

    var css = ctx.Service.GetCssFiles("index.html", "/");

    Assert.Equal(["/assets/main.css"], css);
  }

  [Fact]
  public void GetCssFiles_ReturnsEmptyWhenEntryHasNoCssAnywhere()
  {
    const string manifest = """
    {
      "_dep.js": { "file": "assets/dep.js" },
      "index.html": {
        "file": "assets/main.js",
        "isEntry": true,
        "imports": ["_dep.js"]
      }
    }
    """;
    using var ctx = ManifestTestContext.Create(manifest);

    Assert.Empty(ctx.Service.GetCssFiles("index.html", "/"));
  }

  [Fact]
  public void GetCssFiles_TrimsTrailingSlashFromBasePath()
  {
    using var ctx = ManifestTestContext.Create(SharedChunkManifest);

    var css = ctx.Service.GetCssFiles("index.html", "/dist///");

    Assert.All(css, href => Assert.StartsWith("/dist/assets/", href));
    Assert.DoesNotContain(css, href => href.Contains("//assets"));
  }

  [Fact]
  public void GetEntry_StripsLeadingTilde()
  {
    using var ctx = ManifestTestContext.Create(SharedChunkManifest);

    var entry = ctx.Service.GetEntry("~/index.html");

    Assert.NotNull(entry);
    Assert.Equal("assets/main-espf9ZVg.js", entry!.File);
  }

  [Fact]
  public void GetEntry_ReturnsNullForMissingEntry()
  {
    using var ctx = ManifestTestContext.Create(SharedChunkManifest);

    Assert.Null(ctx.Service.GetEntry("does-not-exist.html"));
  }

  [Fact]
  public void GetJsFile_ResolvesEntryFileWithBasePath()
  {
    using var ctx = ManifestTestContext.Create(SharedChunkManifest);

    Assert.Equal("/assets/checkout-DLgJ9jU3.js", ctx.Service.GetJsFile("checkout/index.html", "/"));
  }

  [Fact]
  public void GetModulePreloadFiles_ReturnsImportedChunkFiles()
  {
    using var ctx = ManifestTestContext.Create(SharedChunkManifest);

    var preloads = ctx.Service.GetModulePreloadFiles(ctx.Service.GetEntry("index.html")!, "/");

    Assert.Equal(["/assets/client.gen-WrdE9yCN.js"], preloads);
  }

  [Fact]
  public void GetModulePreloadFiles_ExcludesTheEntrysOwnFile()
  {
    using var ctx = ManifestTestContext.Create(SharedChunkManifest);

    var preloads = ctx.Service.GetModulePreloadFiles(ctx.Service.GetEntry("index.html")!, "/");

    Assert.DoesNotContain("/assets/main-espf9ZVg.js", preloads);
  }

  [Fact]
  public void GetModulePreloadFiles_WalksNestedImportsDeepestFirst()
  {
    const string manifest = """
    {
      "_leaf.js": { "file": "assets/leaf.js" },
      "_mid.js": { "file": "assets/mid.js", "imports": ["_leaf.js"] },
      "index.html": {
        "file": "assets/main.js",
        "isEntry": true,
        "imports": ["_mid.js"]
      }
    }
    """;
    using var ctx = ManifestTestContext.Create(manifest);

    var preloads = ctx.Service.GetModulePreloadFiles(ctx.Service.GetEntry("index.html")!, "/");

    Assert.Equal(["/assets/leaf.js", "/assets/mid.js"], preloads);
  }

  [Fact]
  public void GetModulePreloadFiles_DeduplicatesAndHandlesCycles()
  {
    const string manifest = """
    {
      "_a.js": { "file": "assets/a.js", "imports": ["_b.js"] },
      "_b.js": { "file": "assets/b.js", "imports": ["_a.js"] },
      "index.html": {
        "file": "assets/main.js",
        "isEntry": true,
        "imports": ["_a.js", "_b.js"]
      }
    }
    """;
    using var ctx = ManifestTestContext.Create(manifest);

    var preloads = ctx.Service.GetModulePreloadFiles(ctx.Service.GetEntry("index.html")!, "/");

    Assert.Equal(2, preloads.Count);
    Assert.Contains("/assets/a.js", preloads);
    Assert.Contains("/assets/b.js", preloads);
  }

  [Fact]
  public void GetModulePreloadFiles_EmptyWhenNoImports()
  {
    const string manifest = """
    { "index.html": { "file": "assets/main.js", "isEntry": true } }
    """;
    using var ctx = ManifestTestContext.Create(manifest);

    Assert.Empty(ctx.Service.GetModulePreloadFiles(ctx.Service.GetEntry("index.html")!, "/"));
  }

  [Fact]
  public void RenderJs_EmitsModulePreloadLinksBeforeEntryScript()
  {
    using var ctx = ManifestTestContext.Create(SharedChunkManifest);
    var entry = ctx.Service.GetEntry("index.html")!;

    var html = RenderToString(ctx.Service.RenderJs(entry, "/"));

    Assert.Contains("<link rel=\"modulepreload\" href=\"/assets/client.gen-WrdE9yCN.js\" />", html);
    Assert.Contains("<script type=\"module\" src=\"/assets/main-espf9ZVg.js\"></script>", html);
    Assert.True(
      html.IndexOf("modulepreload", StringComparison.Ordinal) < html.IndexOf("<script", StringComparison.Ordinal),
      "modulepreload links must precede the entry script tag");
  }

  [Fact]
  public void RenderJs_EmitsSingleScriptTagForEntry()
  {
    // The entry's imported chunks are preloaded, never given their own <script> tag
    // (the module loader fetches them via the entry's import statements).
    using var ctx = ManifestTestContext.Create(SharedChunkManifest);
    var entry = ctx.Service.GetEntry("index.html")!;

    var html = RenderToString(ctx.Service.RenderJs(entry, "/"));

    var scriptCount = html.Split("<script", StringSplitOptions.None).Length - 1;
    Assert.Equal(1, scriptCount);
  }

  [Fact]
  public void GetCssFiles_UsesConfiguredDefaultEntry()
  {
    var options = new ViteManifestOptions { DefaultEntry = "index.html", DefaultBasePath = "/" };
    using var ctx = ManifestTestContext.Create(SharedChunkManifest, options);

    var css = ctx.Service.GetCssFiles();

    Assert.Contains("/assets/client-CYzq3z7c.css", css);
    Assert.Contains("/assets/main-COZv9l4K.css", css);
  }

  [Fact]
  public void GetJsFile_DiscoversSingleHtmlEntryWhenNoDefaultConfigured()
  {
    const string manifest = """
    {
      "_shared-WrdE9yCN.js": { "file": "assets/shared-WrdE9yCN.js" },
      "index.html": { "file": "assets/main-espf9ZVg.js", "isEntry": true, "css": ["assets/main-COZv9l4K.css"] }
    }
    """;
    using var ctx = ManifestTestContext.Create(manifest);

    Assert.Equal("/assets/main-espf9ZVg.js", ctx.Service.GetJsFile());
    Assert.Equal(["/assets/main-COZv9l4K.css"], ctx.Service.GetCssFiles());
    Assert.Empty(ctx.Warnings);
  }

  [Fact]
  public void GetJsFile_DiscoversScriptEntryWhenManifestHasNoHtmlEntry()
  {
    const string manifest = """
    {
      "_shared-WrdE9yCN.js": { "file": "assets/shared-WrdE9yCN.js" },
      "src/main.ts": { "file": "assets/main-espf9ZVg.js", "isEntry": true }
    }
    """;
    using var ctx = ManifestTestContext.Create(manifest);

    Assert.Equal("/assets/main-espf9ZVg.js", ctx.Service.GetJsFile());
  }

  [Fact]
  public void DiscoveredEntry_PrefersHtmlOverScriptEntry()
  {
    const string manifest = """
    {
      "src/main.ts": { "file": "assets/main-AAAAAAAA.js", "isEntry": true },
      "app.html": { "file": "assets/app-BBBBBBBB.js", "isEntry": true }
    }
    """;
    using var ctx = ManifestTestContext.Create(manifest);

    Assert.Equal("/assets/app-BBBBBBBB.js", ctx.Service.GetJsFile());
  }

  [Fact]
  public void DiscoveredEntry_IgnoresNonEntryChunks()
  {
    const string manifest = """
    { "_shared-WrdE9yCN.js": { "file": "assets/shared-WrdE9yCN.js" } }
    """;
    using var ctx = ManifestTestContext.Create(manifest);

    Assert.Null(ctx.Service.GetJsFile());
    Assert.Contains(ctx.Warnings, w => w.Contains("none could be discovered"));
  }

  [Fact]
  public void DiscoveredEntry_WarnsAndPicksDeterministicallyWhenAmbiguous()
  {
    using var ctx = ManifestTestContext.Create(SharedChunkManifest);

    Assert.Equal("/assets/main-espf9ZVg.js", ctx.Service.GetJsFile());
    Assert.Contains(ctx.Warnings, w => w.Contains("declares 2 entries"));
  }

  [Fact]
  public void ConfiguredDefaultEntry_WinsOverDiscovery()
  {
    var options = new ViteManifestOptions { DefaultEntry = "checkout/index.html", DefaultBasePath = "/" };
    using var ctx = ManifestTestContext.Create(SharedChunkManifest, options);

    Assert.Equal("/assets/checkout-DLgJ9jU3.js", ctx.Service.GetJsFile());
    Assert.Empty(ctx.Warnings);
  }

  [Fact]
  public void RenderEntry_EmitsCommentWhenNoEntryCanBeResolved()
  {
    using var ctx = ManifestTestContext.CreateWithoutManifest();

    var html = RenderToString(ctx.Service.RenderEntry());

    Assert.Contains("none could be resolved", html);
  }

  [Fact]
  public void RenderCss_EmitsStylesheetLinksForEntryAndImports()
  {
    using var ctx = ManifestTestContext.Create(SharedChunkManifest);
    var entry = ctx.Service.GetEntry("index.html")!;

    var html = RenderToString(ctx.Service.RenderCss(entry, "/"));

    Assert.Contains("<link rel=\"stylesheet\" href=\"/assets/client-CYzq3z7c.css\" />", html);
    Assert.Contains("<link rel=\"stylesheet\" href=\"/assets/main-COZv9l4K.css\" />", html);
  }

  [Fact]
  public void RenderCss_WithPreloadEmitsPreloadAndNoscriptFallback()
  {
    using var ctx = ManifestTestContext.Create(SharedChunkManifest);
    var entry = ctx.Service.GetEntry("index.html")!;

    var html = RenderToString(ctx.Service.RenderCss(entry, "/", preload: true));

    Assert.Contains("rel=\"preload\"", html);
    Assert.Contains("<noscript>", html);
  }

  [Fact]
  public void RenderEntry_EmitsCommentWhenEntryMissing()
  {
    using var ctx = ManifestTestContext.Create(SharedChunkManifest);

    var html = RenderToString(ctx.Service.RenderEntry("missing.html"));

    Assert.Contains("not found in manifest", html);
  }

  [Fact]
  public void RenderEntry_CssOnly_DoesNotEmitScriptTag()
  {
    using var ctx = ManifestTestContext.Create(SharedChunkManifest);

    var html = RenderToString(ctx.Service.RenderEntry("index.html", "/", assets: ViteAssets.Css));

    Assert.Contains("<link", html);
    Assert.DoesNotContain("<script", html);
  }

  [Fact]
  public void MissingManifest_YieldsEmptyResultsWithoutThrowing()
  {
    using var ctx = ManifestTestContext.CreateWithoutManifest();

    Assert.Null(ctx.Service.GetEntry("index.html"));
    Assert.Empty(ctx.Service.GetCssFiles("index.html", "/"));
    Assert.Null(ctx.Service.GetJsFile("index.html", "/"));
  }

  private static string RenderToString(Microsoft.AspNetCore.Html.IHtmlContent content)
  {
    using var writer = new StringWriter();
    content.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);
    return writer.ToString();
  }
}
