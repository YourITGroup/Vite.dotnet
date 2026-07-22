using Microsoft.AspNetCore.Http;
using Vite.Services;

namespace Vite;

/// <summary>
/// Redirects requests for unhashed asset paths (e.g. <c>/assets/main.css</c>) to their
/// current hashed build output (e.g. <c>/assets/main-COZv9l4K.css</c>) with a 302.
/// The mapping is derived from the Vite manifest, so it always tracks the latest build.
/// Passes through for non-GET/HEAD requests and for paths that are not known unhashed assets.
/// The feature is enabled simply by registering this middleware via
/// <c>app.UseViteUnhashedAssetRedirects()</c>.
/// </summary>
public sealed class ViteUnhashedAssetRedirectMiddleware(RequestDelegate next, IViteManifestService manifest)
{
  public Task InvokeAsync(HttpContext context)
  {
    var request = context.Request;

    if ((HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        && request.Path.HasValue
        && manifest.TryResolveHashedAsset(request.Path.Value, out var hashedPath))
    {
      // 302, not 301: the hashed target changes on every build, so the mapping must
      // never be cached permanently. Preserve the query string on the way through.
      var location = hashedPath + request.QueryString;
      context.Response.Redirect(location, permanent: false);
      return Task.CompletedTask;
    }

    return next(context);
  }
}
