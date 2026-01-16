using System.Collections.Generic;
using System.Linq;
using GenHub.Features.Tools.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.Services.Hosting;

/// <summary>
/// Factory for creating and managing hosting provider instances.
/// </summary>
public class HostingProviderFactory : IHostingProviderFactory
{
    private readonly IReadOnlyList<IHostingProvider> _providers;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostingProviderFactory"/> class.
    /// </summary>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="configuration">The application configuration.</param>
    public HostingProviderFactory(ILoggerFactory loggerFactory, IConfiguration configuration)
    {
        _providers = new List<IHostingProvider>
        {
            new GoogleDriveHostingProvider(loggerFactory.CreateLogger<GoogleDriveHostingProvider>(), configuration),
            new GitHubHostingProvider(loggerFactory.CreateLogger<GitHubHostingProvider>()),
            new DropboxHostingProvider(loggerFactory.CreateLogger<DropboxHostingProvider>()),
            new ManualHostingProvider(),
        };
    }

    /// <inheritdoc/>
    public IReadOnlyList<IHostingProvider> GetAllProviders()
    {
        return _providers;
    }

    /// <inheritdoc/>
    public IHostingProvider? GetProvider(string providerId)
    {
        return _providers.FirstOrDefault(p => p.ProviderId == providerId);
    }

    /// <inheritdoc/>
    public IReadOnlyList<IHostingProvider> GetCatalogHostingProviders()
    {
        return _providers.Where(p => p.SupportsCatalogHosting).ToList();
    }

    /// <inheritdoc/>
    public IReadOnlyList<IHostingProvider> GetArtifactHostingProviders()
    {
        return _providers.Where(p => p.SupportsArtifactHosting).ToList();
    }
}
