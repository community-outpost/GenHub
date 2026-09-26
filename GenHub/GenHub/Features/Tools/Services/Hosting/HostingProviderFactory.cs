using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Features.Tools.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

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
    /// <param name="configurationProvider">Optional configuration provider service.</param>
    /// <param name="localizationService">Optional localization service for provider messages.</param>
    /// <param name="credentialStore">Optional encrypted credential store for OAuth tokens.</param>
    public HostingProviderFactory(
        ILoggerFactory loggerFactory,
        IHttpClientFactory httpClientFactory,
        IConfigurationProviderService? configurationProvider = null,
        ILocalizationService? localizationService = null,
        IHostingCredentialStore? credentialStore = null)
    {
        _providers = new List<IHostingProvider>
        {
            new GoogleDriveHostingProvider(loggerFactory.CreateLogger<GoogleDriveHostingProvider>(), configurationProvider, localizationService, credentialStore),
            new GitHubHostingProvider(loggerFactory.CreateLogger<GitHubHostingProvider>()),
            new DropboxHostingProvider(loggerFactory.CreateLogger<DropboxHostingProvider>(), httpClientFactory, localizationService),
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
