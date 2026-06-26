using Microsoft.Extensions.Configuration;
using Vite.Services;
using Vite.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceExtensions
{
  extension(IServiceCollection services)
  {
    /// <summary>
    /// Registers the Vite manifest service used by the &lt;vite&gt; tag helper.
    /// Registered as a singleton so the manifest is read and parsed once and
    /// cached for the lifetime of the application.
    /// </summary>
    /// <param name="configure">Optional configuration of <see cref="ViteManifestOptions"/> (e.g. the default entry and base path).</param>
    public IServiceCollection AddViteManifest(Action<ViteManifestOptions>? configure = null)
    {
      if (configure is not null)
      {
        services.Configure(configure);
      }

      return services.AddViteManifestCore();
    }

    /// <summary>
    /// Registers the Vite manifest service, binding <see cref="ViteManifestOptions"/> from the
    /// "<see cref="ViteManifestOptions.SectionName"/>" configuration section (e.g. appsettings.json).
    /// </summary>
    /// <param name="configuration">Application configuration to bind options from.</param>
    /// <param name="configure">Optional code-based overrides applied on top of the bound configuration.</param>
    public IServiceCollection AddViteManifest(IConfiguration configuration, Action<ViteManifestOptions>? configure = null)
    {
      services.Configure<ViteManifestOptions>(configuration.GetSection(ViteManifestOptions.SectionName));

      if (configure is not null)
      {
        services.Configure(configure);
      }

      return services.AddViteManifestCore();
    }

    private IServiceCollection AddViteManifestCore()
    {
      services.AddSingleton<IViteManifestService, ViteManifestService>();
      return services;
    }
  }
}
