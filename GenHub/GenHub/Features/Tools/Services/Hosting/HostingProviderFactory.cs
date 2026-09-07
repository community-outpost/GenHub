using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using GenHub.Features.Tools.Interfaces;
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
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    public HostingProviderFactory(ILoggerFactory loggerFactory, IHttpClientFactory httpClientFactory)
    {
        _providers = new List<IHostingProvider>
        {
            new GoogleDriveHostingProvider(loggerFactory.CreateLogger<GoogleDriveHostingProvider>()),
            new GitHubHostingProvider(loggerFactory.CreateLogger<GitHubHostingProvider>()),
            new DropboxHostingProvider(loggerFactory.CreateLogger<DropboxHostingProvider>(), httpClientFactory),
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
