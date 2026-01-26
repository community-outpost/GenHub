using System;
using System.Net.Http;
using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Parsers;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Services.Content;
using GenHub.Core.Services.Providers;
using GenHub.Features.Content.Services;
using GenHub.Features.Content.Services.CommunityOutpost;
using GenHub.Features.Content.Services.ContentDeliverers;
using GenHub.Features.Content.Services.ContentDiscoverers;
using GenHub.Features.Content.Services.ContentProviders;
using GenHub.Features.Content.Services.ContentResolvers;
using GenHub.Features.Content.Services.GeneralsOnline;
using GenHub.Features.Content.Services.GitHub;
using GenHub.Features.Content.Services.Parsers;
using GenHub.Features.Content.Services.Publishers;
using GenHub.Features.Content.Services.Tools;
using GenHub.Features.Downloads.Services;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.GitHub.Services;
using GenHub.Features.Manifest;
using GenHub.Features.Storage.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods for registering content pipeline services.
/// </summary>
public static class ContentPipelineModule
{
    /// <summary>
    /// Registers content pipeline services for dependency injection.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <summary>
    /// Registers core and all content pipeline services into the provided service collection.
    /// </summary>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddContentPipelineServices(this IServiceCollection services)
    {
        // Register core services
        AddCoreServices(services);

        // Register content pipelines
        AddGitHubPipeline(services);
        AddGeneralsOnlinePipeline(services);
        AddCommunityOutpostPipeline(services);
        AddCNCLabsPipeline(services);
        AddModDBPipeline(services);
        AddAODMapsPipeline(services);
        AddLocalFileSystemPipeline(services);
        AddSharedComponents(services);

        return services;
    }

    /// <summary>
    /// Registers core services required by all pipelines.
    /// <summary>
    /// Registers core shared services and infrastructure required by all content pipelines.
    /// </summary>
    /// <remarks>
    /// Registers foundational services such as the content orchestrator, hash providers, memory and dynamic caches,
    /// HTTP client factories (including a named client for Generals Online), content storage and manifest pool,
    /// provider/ catalog parsers and factories, content caching, GitHub API client, local content service,
    /// publisher subscription and catalog services, the generic catalog pipeline types, and a subscription confirmation view model registration.
    /// </remarks>
    private static void AddCoreServices(IServiceCollection services)
    {
        // Register content orchestrator
        services.AddSingleton<IContentOrchestrator, ContentOrchestrator>();

        // Register core hash provider
        var hashProvider = new Sha256HashProvider();
        services.AddSingleton<IFileHashProvider>(hashProvider);
        services.AddSingleton<IStreamHashProvider>(hashProvider);

        // Register memory cache
        services.AddMemoryCache();

        // Register HTTP client factory for content providers
        services.AddHttpClient();

        // Register named HTTP client for Generals Online
        services.AddHttpClient(GeneralsOnlineConstants.PublisherType, static httpClient =>
        {
            httpClient.Timeout = TimeSpan.FromSeconds(30);
        });

        // Register core storage and manifest services
        services.AddSingleton<IContentStorageService>(sp =>
        {
            var configService = sp.GetRequiredService<IConfigurationProviderService>();
            var logger = sp.GetRequiredService<ILogger<ContentStorageService>>();
            var casService = sp.GetRequiredService<ICasService>();

            // Get application data path where manifests metadata is stored
            var storageRoot = configService.GetApplicationDataPath();
            var referenceTracker = sp.GetRequiredService<CasReferenceTracker>();

            return new ContentStorageService(storageRoot, logger, casService, referenceTracker);
        });
        services.AddScoped<IContentManifestPool, ContentManifestPool>();

        // Register provider definition loader for data-driven provider configuration
        services.AddSingleton<IProviderDefinitionLoader, ProviderDefinitionLoader>();

        // Register catalog parser factory and parsers
        services.AddSingleton<ICatalogParserFactory, CatalogParserFactory>();
        services.AddSingleton<ICatalogParser, GenPatcherDatCatalogParser>();
        services.AddSingleton<ICatalogParser, GeneralsOnlineJsonCatalogParser>();

        // Register cache
        services.AddSingleton<IDynamicContentCache, MemoryDynamicContentCache>();

        // Register content cache service for caching parsed content data
        services.AddSingleton<IContentCacheService, ContentCacheService>();

        // Register Octokit GitHub client
        services.AddSingleton<Octokit.IGitHubClient>(sp =>
        {
            return new Octokit.GitHubClient(new Octokit.ProductHeaderValue("GenHub"));
        });

        // Register GitHub API client
        services.AddSingleton<IGitHubApiClient, OctokitGitHubApiClient>();

        // Register Local Content Service
        services.AddTransient<ILocalContentService, LocalContentService>();

        // Register publisher subscription store for creator catalog management
        services.AddSingleton<IPublisherSubscriptionStore, GenHub.Features.Content.Services.Catalog.PublisherSubscriptionStore>();

        // Register catalog parser and version selector
        services.AddSingleton<IPublisherCatalogParser, GenHub.Features.Content.Services.Catalog.JsonPublisherCatalogParser>();
        services.AddSingleton<IVersionSelector, GenHub.Features.Content.Services.Catalog.VersionSelector>();
        services.AddSingleton<IPublisherCatalogRefreshService, GenHub.Features.Content.Services.Catalog.PublisherCatalogRefreshService>();

        // Register generic catalog pipeline (transient for per-subscription instances)
        services.AddTransient<GenHub.Features.Content.Services.Catalog.GenericCatalogDiscoverer>();
        services.AddTransient<GenHub.Features.Content.Services.Catalog.GenericCatalogResolver>();
        services.AddTransient<IContentResolver>(sp => sp.GetRequiredService<GenHub.Features.Content.Services.Catalog.GenericCatalogResolver>());

        // Register ViewModels for catalog management
        services.AddTransient<GenHub.Features.Content.ViewModels.Catalog.SubscriptionConfirmationViewModel>(sp =>
        {
            // Note: This is usually created via ActivatorUtilities to pass the URL,
            // but we register the type itself just in case or for simple resolution.
            return null!; // We'll use ActivatorUtilities.CreateInstance in App.axaml.cs
        });
    }

    /// <summary>
    /// Registers GitHub content pipeline services.
    /// <summary>
    /// Registers GitHub-specific services required by the content pipeline, including providers, discoverers, resolver, deliverer, publisher manifest factories, and the SuperHackers update service.
    /// </summary>
    private static void AddGitHubPipeline(IServiceCollection services)
    {
        // Register GitHub content provider
        services.AddSingleton<GitHubContentProvider>();
        services.AddSingleton<IContentProvider>(sp => sp.GetRequiredService<GitHubContentProvider>());

        // Register SuperHackers provider (uses GitHub discoverer/resolver/deliverer)
        services.AddSingleton<SuperHackersProvider>();
        services.AddSingleton<IContentProvider>(sp => sp.GetRequiredService<SuperHackersProvider>());

        // Register GitHub discoverers (both concrete and interface registrations)
        services.AddSingleton<GitHubDiscoverer>();
        services.AddSingleton<GitHubReleasesDiscoverer>();
        services.AddSingleton<GitHubTopicsDiscoverer>();
        services.AddSingleton<IContentDiscoverer>(sp => sp.GetRequiredService<GitHubDiscoverer>());
        services.AddSingleton<IContentDiscoverer>(sp => sp.GetRequiredService<GitHubReleasesDiscoverer>());
        services.AddSingleton<IContentDiscoverer>(sp => sp.GetRequiredService<GitHubTopicsDiscoverer>());

        // Register GitHub resolver
        services.AddSingleton<GitHubResolver>();
        services.AddSingleton<IContentResolver>(sp => sp.GetRequiredService<GitHubResolver>());

        // Register GitHub deliverer
        services.AddSingleton<GitHubContentDeliverer>();
        services.AddSingleton<IContentDeliverer>(sp => sp.GetRequiredService<GitHubContentDeliverer>());

        // Register SuperHackers manifest factory
        services.AddSingleton<SuperHackersManifestFactory>();
        services.AddSingleton<IPublisherManifestFactory>(sp => sp.GetRequiredService<SuperHackersManifestFactory>());

        // Register SuperHackers update service
        services.AddSingleton<SuperHackersUpdateService>();

        // Register GitHub generic manifest factory
        services.AddTransient<GitHubManifestFactory>();
        services.AddTransient<IPublisherManifestFactory>(sp => sp.GetRequiredService<GitHubManifestFactory>());
    }

    /// <summary>
    /// Registers Generals Online content pipeline services.
    /// <summary>
    /// Registers all dependency-injection services required by the Generals Online content pipeline.
    /// </summary>
    private static void AddGeneralsOnlinePipeline(IServiceCollection services)
    {
        // Register Generals Online provider
        services.AddSingleton<GeneralsOnlineProvider>();
        services.AddSingleton<IContentProvider>(sp => sp.GetRequiredService<GeneralsOnlineProvider>());

        // Register Generals Online discoverer (concrete and interface)
        services.AddSingleton<GeneralsOnlineDiscoverer>();
        services.AddSingleton<IContentDiscoverer>(sp => sp.GetRequiredService<GeneralsOnlineDiscoverer>());

        // Register Generals Online resolver (concrete and interface)
        services.AddSingleton<GeneralsOnlineResolver>();
        services.AddSingleton<IContentResolver>(sp => sp.GetRequiredService<GeneralsOnlineResolver>());

        // Register Generals Online deliverer
        services.AddSingleton<GeneralsOnlineDeliverer>();
        services.AddSingleton<IContentDeliverer>(sp => sp.GetRequiredService<GeneralsOnlineDeliverer>());

        // Register Generals Online manifest factory
        services.AddSingleton<GeneralsOnlineManifestFactory>();
        services.AddSingleton<IPublisherManifestFactory>(sp => sp.GetRequiredService<GeneralsOnlineManifestFactory>());

        // Register Generals Online update service
        services.AddSingleton<GeneralsOnlineUpdateService>();

        // Register Generals Online profile reconciler
        services.AddSingleton<IGeneralsOnlineProfileReconciler, GeneralsOnlineProfileReconciler>();
    }

    /// <summary>
    /// Registers Community Outpost content pipeline services.
    /// <summary>
    /// Registers Community Outpost content-pipeline services into the provided service collection.
    /// </summary>
    /// <remarks>
    /// Adds concrete implementations and their interface mappings for the Community Outpost provider, discoverer, resolver, deliverer, manifest factory, and update service.
    /// </remarks>
    private static void AddCommunityOutpostPipeline(IServiceCollection services)
    {
        // Register Community Outpost provider
        services.AddSingleton<CommunityOutpostProvider>();
        services.AddSingleton<IContentProvider>(sp => sp.GetRequiredService<CommunityOutpostProvider>());

        // Register Community Outpost discoverer (concrete and interface)
        services.AddSingleton<CommunityOutpostDiscoverer>();
        services.AddSingleton<IContentDiscoverer>(sp => sp.GetRequiredService<CommunityOutpostDiscoverer>());

        // Register Community Outpost resolver
        services.AddSingleton<CommunityOutpostResolver>();
        services.AddSingleton<IContentResolver>(sp => sp.GetRequiredService<CommunityOutpostResolver>());

        // Register Community Outpost deliverer
        services.AddSingleton<CommunityOutpostDeliverer>();
        services.AddSingleton<IContentDeliverer>(sp => sp.GetRequiredService<CommunityOutpostDeliverer>());

        // Register Community Outpost manifest factory
        services.AddSingleton<CommunityOutpostManifestFactory>();
        services.AddSingleton<IPublisherManifestFactory>(sp => sp.GetRequiredService<CommunityOutpostManifestFactory>());

        // Register Community Outpost update service
        services.AddSingleton<CommunityOutpostUpdateService>();
    }

    /// <summary>
    /// Registers CNCLabs content pipeline services.
    /// <summary>
    /// Registers CNCLabs content pipeline components (content provider, map discoverer, map resolver, and publisher manifest factory) as singletons in the DI container.
    /// </summary>
    private static void AddCNCLabsPipeline(IServiceCollection services)
    {
        // Register CNCLabs content provider
        services.AddSingleton<CNCLabsContentProvider>();
        services.AddSingleton<IContentProvider>(sp => sp.GetRequiredService<CNCLabsContentProvider>());

        // Register CNCLabs discoverer (concrete and interface)
        services.AddSingleton<CNCLabsMapDiscoverer>();
        services.AddSingleton<IContentDiscoverer>(sp => sp.GetRequiredService<CNCLabsMapDiscoverer>());

        // Register CNCLabs resolver
        services.AddSingleton<CNCLabsMapResolver>();
        services.AddSingleton<IContentResolver>(sp => sp.GetRequiredService<CNCLabsMapResolver>());

        // Register CNCLabs manifest factory
        services.AddSingleton<CNCLabsManifestFactory>();
        services.AddSingleton<IPublisherManifestFactory>(sp => sp.GetRequiredService<CNCLabsManifestFactory>());
    }

    /// <summary>
    /// Registers ModDB content pipeline services.
    /// <summary>
    /// Registers ModDB-specific content-pipeline services and configures the named HttpClient used to access ModDB.
    /// </summary>
    /// <param name="services">The IServiceCollection to which ModDB pipeline services (HttpClient, Playwright page parser, discoverer, resolver, manifest factory, and content provider) will be added.</param>
    private static void AddModDBPipeline(IServiceCollection services)
    {
        // Register named HTTP client for ModDB
        services.AddHttpClient(ModDBConstants.PublisherPrefix, httpClient =>
        {
            httpClient.BaseAddress = new Uri(ModDBConstants.BaseUrl);
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            // Add comprehensive browser headers to bypass 403 Forbidden
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        }).ConfigurePrimaryHttpMessageHandler(() =>
        {
            return new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate | System.Net.DecompressionMethods.Brotli,
                CookieContainer = new System.Net.CookieContainer(),
                UseCookies = true,
            };
        });

        // Register Playwright service for web page parsing (singleton for shared browser instance)
        services.AddSingleton<IPlaywrightService, PlaywrightService>();

        // Register ModDB page parser
        services.AddSingleton<ModDBPageParser>();
        services.AddSingleton<IWebPageParser>(sp => sp.GetRequiredService<ModDBPageParser>());

        // Register ModDB discoverer (concrete and interface) with named HttpClient
        // Register ModDB discoverer (concrete and interface)
        services.AddSingleton<ModDBDiscoverer>();
        services.AddSingleton<IContentDiscoverer>(sp => sp.GetRequiredService<ModDBDiscoverer>());

        // Register ModDB resolver
        services.AddSingleton<ModDBResolver>();
        services.AddSingleton<IContentResolver>(sp => sp.GetRequiredService<ModDBResolver>());

        // Register ModDB manifest factory
        services.AddSingleton<ModDBManifestFactory>();
        services.AddSingleton<IPublisherManifestFactory>(sp => sp.GetRequiredService<ModDBManifestFactory>());

        // Register ModDB content provider
        services.AddSingleton<GenHub.Features.Content.Services.ContentProviders.ModDBContentProvider>();
        services.AddSingleton<IContentProvider>(sp => sp.GetRequiredService<GenHub.Features.Content.Services.ContentProviders.ModDBContentProvider>());
    }

    /// <summary>
    /// Registers Local File System content pipeline services.
    /// <summary>
    /// Registers Local File System content pipeline services — provider, discoverer, manifest resolver, and deliverer — into the provided IServiceCollection.
    /// </summary>
    private static void AddLocalFileSystemPipeline(IServiceCollection services)
    {
        // Register Local File System content provider
        services.AddSingleton<LocalFileSystemContentProvider>();
        services.AddSingleton<IContentProvider>(sp => sp.GetRequiredService<LocalFileSystemContentProvider>());

        // Register File System discoverer
        services.AddSingleton<FileSystemDiscoverer>();
        services.AddSingleton<IContentDiscoverer>(sp => sp.GetRequiredService<FileSystemDiscoverer>());

        // Register Local Manifest resolver
        services.AddSingleton<LocalManifestResolver>();
        services.AddSingleton<IContentResolver>(sp => sp.GetRequiredService<LocalManifestResolver>());

        // Register File System deliverer
        services.AddSingleton<FileSystemDeliverer>();
        services.AddSingleton<IContentDeliverer>(sp => sp.GetRequiredService<FileSystemDeliverer>());
    }

    /// <summary>
    /// Registers AODMaps content pipeline services.
    /// <summary>
    /// Registers the AODMaps content-pipeline components and a named HttpClient into the provided service collection.
    /// </summary>
    /// <remarks>
    /// Adds a named "AODMaps" HttpClient and singleton registrations for the page parser, IWebPageParser, discoverer, resolver, content provider, and publisher manifest factory.
    /// </remarks>
    private static void AddAODMapsPipeline(IServiceCollection services)
    {
        // Register named HttpClient for AODMaps
        services.AddHttpClient("AODMaps", client =>
        {
            client.BaseAddress = new Uri(AODMapsConstants.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent", "GenHub/1.0");
        });

        // Register AODMaps page parser (Concrete)
        services.AddSingleton<AODMapsPageParser>();

        // Register as interface as well if needed by generic components, but be careful of overlapping
        services.AddSingleton<IWebPageParser>(sp => sp.GetRequiredService<AODMapsPageParser>());

        // Register AODMaps discoverer
        services.AddSingleton<GenHub.Features.Content.Services.ContentDiscoverers.AODMapsDiscoverer>();
        services.AddSingleton<IContentDiscoverer>(sp => sp.GetRequiredService<GenHub.Features.Content.Services.ContentDiscoverers.AODMapsDiscoverer>());

        // Register AODMaps resolver
        services.AddSingleton<GenHub.Features.Content.Services.ContentResolvers.AODMapsResolver>();
        services.AddSingleton<IContentResolver>(sp => sp.GetRequiredService<GenHub.Features.Content.Services.ContentResolvers.AODMapsResolver>());

        // Register AODMaps content provider
        services.AddSingleton<GenHub.Features.Content.Services.ContentProviders.AODMapsContentProvider>();
        services.AddSingleton<IContentProvider>(sp => sp.GetRequiredService<GenHub.Features.Content.Services.ContentProviders.AODMapsContentProvider>());

        // Register AODMaps manifest factory
        services.AddSingleton<GenHub.Features.Content.Services.Publishers.AODMapsManifestFactory>();
        services.AddSingleton<IPublisherManifestFactory>(sp => sp.GetRequiredService<GenHub.Features.Content.Services.Publishers.AODMapsManifestFactory>());
    }

    /// <summary>
    /// Registers shared components used across multiple pipelines.
    /// <summary>
    /// Registers services and shared components used by multiple content pipelines.
    /// </summary>
    /// <remarks>
    /// Registers:
    /// - HttpContentDeliverer and its IContentDeliverer mapping,
    /// - PublisherManifestFactoryResolver,
    /// - IContentPipelineFactory (ContentPipelineFactory),
    /// - PublisherCardViewModel (transient),
    /// - IContentValidator (ContentValidator),
    /// - IContentStateService (scoped ContentStateService).
    /// </remarks>
    private static void AddSharedComponents(IServiceCollection services)
    {
        // Register shared deliverers
        services.AddSingleton<HttpContentDeliverer>();
        services.AddSingleton<IContentDeliverer>(sp => sp.GetRequiredService<HttpContentDeliverer>());

        // Register publisher manifest factory resolver
        services.AddSingleton<PublisherManifestFactoryResolver>();

        // Register content pipeline factory for provider-based component lookup
        services.AddSingleton<IContentPipelineFactory, ContentPipelineFactory>();
        services.AddTransient<PublisherCardViewModel>();

        // Register content orchestrator and validator
        services.AddSingleton<IContentValidator, ContentValidator>();

        // Register content state service for determining download/update state
        services.AddScoped<IContentStateService, ContentStateService>();
    }
}