# Vite.dotnet

Resolves [Vite](https://vitejs.dev/) build-manifest entries to their hashed output files and renders the matching `<link>` / `<script>` tags in Razor views — so cache-busted asset filenames (e.g. `index.2A98uGZn.js`) never have to be hard-coded.

The manifest (`manifest.json`) is read and parsed **once** and cached for the lifetime of the application (it is a build artifact that does not change at runtime), so it is not re-read on every page render.

## How it works

A production Vite build emits a manifest that maps each logical entry (e.g. `index.html`) to its hashed output file and associated CSS:

```json
{
  "index.html": {
    "file": "js/index.2A98uGZn.js",
    "css": ["css/index.CG5GY9Tq.css"],
    "isEntry": true
  }
}
```

`ViteManifestService` loads this manifest from `{WebRootPath}/.vite/manifest.json`, resolves an entry, prefixes the files with a configurable base path, and renders the tags. The `<vite>` tag helper is a thin wrapper over the service.

## Setup

Two things wire it up:

**1. Register the service** (in `Program.cs`):

```csharp
builder.Services.AddViteManifest(builder.Configuration);
```

**2. Register the tag helper** (in `_ViewImports.cshtml`):

```cshtml
@addTagHelper *, Vite
```

## Configuration

Options bind from the `ViteManifest` section of `appsettings.json`:

```json
"ViteManifest": {
  "DefaultEntry": "index.html",
  "DefaultBasePath": "/"
}
```

| Option            | Default        | Description                                                              |
| ----------------- | -------------- | ------------------------------------------------------------------------ |
| `DefaultEntry`    | `index.html`   | The logical entry assumed by the parameterless service getters.          |
| `DefaultBasePath` | `/`            | The base path hashed assets are served from when none is supplied.       |

Registration overloads (`ViteServiceExtensions`):

```csharp
// Defaults only
builder.Services.AddViteManifest();

// Bind from appsettings ("ViteManifest" section)
builder.Services.AddViteManifest(builder.Configuration);

// Code-based configuration
builder.Services.AddViteManifest(o => o.DefaultBasePath = "/dist/");

// Bind from appsettings, then override in code (code wins)
builder.Services.AddViteManifest(builder.Configuration, o => o.DefaultBasePath = "/dist/");
```

## Usage — the `<vite>` tag helper

```cshtml
@* Render both CSS and JS for an entry, with CSS preloaded *@
<vite entry="~/index.html" base-path="/" preload-css="true" />

@* CSS only — e.g. in <head> *@
<vite entry="~/index.html" base-path="/" preload-css="true" assets="Css" />

@* JS only — e.g. at the bottom of <body> *@
<vite entry="~/index.html" base-path="/" assets="Js" />
```

| Attribute     | Type         | Default           | Description                                                       |
| ------------- | ------------ | ----------------- | ----------------------------------------------------------------- |
| `entry`       | string       | `DefaultEntry`    | Logical entry name. A leading `~/` is tolerated and stripped. Falls back to the configured default when omitted. |
| `base-path`   | string       | `DefaultBasePath` | Base path the hashed assets are served from. Falls back to the configured default when omitted. |
| `preload-css` | bool         | `false`           | Emit `rel="preload"` links with a `<noscript>` fallback.          |
| `assets`      | `ViteAssets` | `All`             | Which tags to render: `Css`, `Js`, or `All`.                      |
| `dev-server`  | string       | `""`              | Vite dev-server `host:port`; only used in the Development env.    |

With both defaulted from options, the tag can be reduced to just `<vite />` (or `<vite assets="Css" />`):

```cshtml
<vite />                          @* default entry + base path, both CSS and JS *@
<vite preload-css="true" />       @* …with CSS preloaded *@
```

> **Note on `~/`:** Razor does not expand `~/` for custom tag-helper attributes (only the built-in `script`/`link` helpers resolve it). The service strips a leading `~/` itself so the value matches the manifest key (`index.html`).

## Usage — from other services

Inject `IViteManifestService` to retrieve resolved asset URLs (for CSP source lists, preload hints, passing to another renderer, etc.):

```csharp
// Uses DefaultEntry + DefaultBasePath from options
string? js   = manifest.GetJsFile();                  // "/js/index.2A98uGZn.js"
IReadOnlyList<string> css = manifest.GetCssFiles();   // ["/css/index.CG5GY9Tq.css"]

// Explicit entry / base path
string? js2 = manifest.GetJsFile("index.html", "/");
var css2    = manifest.GetCssFiles("index.html", "/");

// Resolve once, reuse
var entry = manifest.GetEntry("index.html");
if (entry is not null)
{
    var files   = manifest.GetCssFiles(entry, "/");
    var content = manifest.RenderJs(entry, "/");      // IHtmlContent
}
```

Returned URLs are prefixed with the supplied base path. Pass `""` as the base path to get the bare manifest-relative file (e.g. `js/index.2A98uGZn.js`), useful for locating the physical file on disk.

### API surface (`IViteManifestService`)

| Member                                                                   | Returns                | Purpose                                            |
| ------------------------------------------------------------------------ | ---------------------- | -------------------------------------------------- |
| `GetEntry(entry)`                                                        | `ViteManifestEntry?`   | Resolve + cache lookup (`~/` tolerated).           |
| `GetCssFiles()` / `(entry, basePath)` / `(ViteManifestEntry, basePath)`  | `IReadOnlyList<string>`| Resolved CSS URLs.                                 |
| `GetJsFile()` / `(entry, basePath)` / `(ViteManifestEntry, basePath)`    | `string?`              | Resolved JS URL.                                   |
| `RenderCss(entry, basePath, preload)`                                    | `IHtmlContent`         | CSS `<link>` tags only.                            |
| `RenderJs(entry, basePath)`                                              | `IHtmlContent`         | Module `<script>` tag only.                        |
| `RenderEntry(entry, basePath, preloadCss, assets, devServer)`            | `IHtmlContent`         | Full render; handles dev-server + not-found cases. |

## Development mode

When the environment is `Development` **and** a `dev-server` is supplied, `RenderEntry` skips the manifest entirely and points straight at the Vite dev server for HMR:

```html
<script type="module" src="http://localhost:5173/index.html"></script>
```

The Vite dev server injects CSS through the module script, so a single tag covers both — render with `assets="Js"` if you split head/body in dev. The standalone `RenderCss`/`RenderJs`/`Get*` methods operate purely on the cached manifest (production assets) and do not have a dev-server path.

## Publishing — the `.vite` folder gotcha

Vite writes the manifest to `wwwroot/.vite/manifest.json`. ASP.NET Core's **Static Web Assets** pipeline ignores dot-prefixed files/folders, so the manifest is silently dropped from `dotnet publish`. The entrypoint project should include a post-publish MSBuild target (`CopyViteManifestToPublish`) to copy it back into the publish output. Keeping the file under `.vite/` also means the static-files middleware will not serve it publicly.

```xml

  <!--
    The Vite manifest lives in wwwroot/.vite/. ASP.NET Core's Static Web Assets
    pipeline ignores dot-prefixed files/folders, so it is dropped from publish.
    Copy it back into the publish output explicitly.
  -->
  <Target Name="CopyViteManifestToPublish" AfterTargets="Publish" Condition="Exists('$(MSBuildProjectDirectory)\wwwroot\.vite\manifest.json')">
    <Copy SourceFiles="$(MSBuildProjectDirectory)\wwwroot\.vite\manifest.json" DestinationFolder="$(PublishDir)wwwroot\.vite" SkipUnchangedFiles="true" />
  </Target>
```

## Project structure

```
Vite/
├── Configuration/
│   └── ViteManifestOptions.cs      # bindable options (+ SectionName)
├── Models/
│   ├── ViteAssets.cs               # [Flags] enum: Css | Js | All
│   └── ViteManifestEntry.cs        # one manifest entry (file, css, imports)
├── Services/
│   ├── IViteManifestService.cs
│   └── ViteManifestService.cs      # singleton; lazy, cached manifest load
├── ViteServiceExtensions.cs  # AddViteManifest(...) registration
└── ViteTagHelper.cs                # the <vite> tag helper
```
