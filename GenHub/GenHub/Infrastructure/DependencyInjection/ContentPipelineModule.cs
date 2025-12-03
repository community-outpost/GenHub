using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Features.Content.Services;
using GenHub.Features.Content.Services.ContentDeliverers;
using GenHub.Features.Content.Services.ContentDiscoverers;
using GenHub.Features.Content.Services.ContentProviders;
using GenHub.Features.Content.Services.ContentResolvers;
using GenHub.Features.Content.Services.GeneralsOnline;
using GenHub.Features.Content.Services.Publishers;
using GenHub.Features.GitHub.Services;
using GenHub.Features.Manifest;
using Microsoft.Extensions.DependencyInjection;
using System;

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
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddContentPipelineServices(this IServiceCollection services)
    {
        // Register core services
        AddCoreServices(services);

        // Register content pipelines
        AddGitHubPipeline(services);
        AddGeneralsOnlinePipeline(services);
        AddCNCLabsPipeline(services);
        AddModDBPipeline(services);
        AddLocalFileSystemPipeline(services);
        AddSharedComponents(services);

        return services;
    }

    /// <summary>
    /// Registers core services required by all pipelines.
    /// </summary>
    private static void AddCoreServices(IServiceCollection services)
    {
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
        services.AddSingleton<IContentStorageService, ContentStorageService>();
        services.AddScoped<IContentManifestPool, ContentManifestPool>();

        // Register core orchestrator
        services.AddSingleton<IContentOrchestrator, ContentOrchestrator>();

        // Register cache
        services.AddSingleton<IDynamicContentCache, MemoryDynamicContentCache>();

        // Register Octokit GitHub client
        services.AddSingleton<Octokit.IGitHubClient>(sp =>
        {
            var client = new Octokit.GitHubClient(new Octokit.ProductHeaderValue("GenHub"));
            return client;
        });

        // Register GitHub API client
        services.AddSingleton<IGitHubApiClient, OctokitGitHubApiClient>();
    }

    /// <summary>
    /// Registers GitHub content pipeline services.
    /// </summary>
    private static void AddGitHubPipeline(IServiceCollection services)
    {
        // Register GitHub content provider
        services.AddTransient<IContentProvider, GitHubContentProvider>();

        // Register GitHub discoverers
        services.AddTransient<IContentDiscoverer, GitHubDiscoverer>();
        services.AddTransient<IContentDiscoverer, GitHubReleasesDiscoverer>();

        // Register GitHub resolver
        services.AddTransient<IContentResolver, GitHubResolver>();
    }

    /// <summary>
    /// Registers Generals Online content pipeline services.
    /// </summary>
    private static void AddGeneralsOnlinePipeline(IServiceCollection services)
    {
        // Register Generals Online provider
        services.AddTransient<IContentProvider, GeneralsOnlineProvider>();

        // Register Generals Online discoverer
        services.AddTransient<IContentDiscoverer, GeneralsOnlineDiscoverer>();

        // Register Generals Online resolver
        services.AddTransient<IContentResolver, GeneralsOnlineResolver>();

        // Register Generals Online deliverer
        services.AddTransient<IContentDeliverer, GeneralsOnlineDeliverer>();

        // Register Generals Online manifest factory
        services.AddTransient<GeneralsOnlineManifestFactory>();

        // Register Generals Online update service
        services.AddSingleton<GeneralsOnlineUpdateService>();
    }

    /// <summary>
    /// Registers CNCLabs content pipeline services.
    /// </summary>
    private static void AddCNCLabsPipeline(IServiceCollection services)
    {
        // Register CNCLabs content provider
        services.AddTransient<IContentProvider, CNCLabsContentProvider>();

        // Register CNCLabs discoverer
        services.AddTransient<IContentDiscoverer, CNCLabsMapDiscoverer>();

        // Register CNCLabs resolver
        services.AddTransient<IContentResolver, CNCLabsMapResolver>();

        // Register CNCLabs manifest factory
        services.AddTransient<CNCLabsManifestFactory>();
        services.AddTransient<IPublisherManifestFactory, CNCLabsManifestFactory>();
    }

    /// <summary>
    /// Registers ModDB content pipeline services.
    /// </summary>
    private static void AddModDBPipeline(IServiceCollection services)
    {
        // Register named HTTP client for ModDB
        services.AddHttpClient(ModDBConstants.PublisherPrefix, static httpClient =>
        {
            httpClient.Timeout = TimeSpan.FromSeconds(45); // ModDB can be slower
            httpClient.DefaultRequestHeaders.Add("User-Agent", "GenHub/1.0");
        });

        // Register ModDB discoverer
        services.AddTransient<IContentDiscoverer, ModDBDiscoverer>();

        // Register ModDB resolver
        services.AddTransient<IContentResolver, ModDBResolver>();

        // Register ModDB manifest factory
        services.AddTransient<ModDBManifestFactory>();
        services.AddTransient<IPublisherManifestFactory, ModDBManifestFactory>();
    }

    /// <summary>
    /// Registers Local File System content pipeline services.
    /// </summary>
    private static void AddLocalFileSystemPipeline(IServiceCollection services)
    {
        // Register Local File System content provider
        services.AddTransient<IContentProvider, LocalFileSystemContentProvider>();

        // Register File System discoverer
        services.AddTransient<IContentDiscoverer, FileSystemDiscoverer>();

        // Register Local Manifest resolver
        services.AddTransient<IContentResolver, LocalManifestResolver>();

        // Register File System deliverer
        services.AddTransient<IContentDeliverer, FileSystemDeliverer>();
    }

    /// <summary>
    /// Registers shared components used across multiple pipelines.
    /// </summary>
    private static void AddSharedComponents(IServiceCollection services)
    {
        // Register shared deliverers
        services.AddTransient<IContentDeliverer, HttpContentDeliverer>();

        // Register publisher manifest factory resolver
        services.AddTransient<PublisherManifestFactoryResolver>();
    }
}
