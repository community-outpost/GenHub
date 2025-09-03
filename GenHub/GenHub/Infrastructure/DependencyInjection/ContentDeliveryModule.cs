using System;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Features.Content.Services;
using GenHub.Features.Content.Services.ContentDeliverers;
using GenHub.Features.Content.Services.ContentDiscoverers;
using GenHub.Features.Content.Services.ContentProviders;
using GenHub.Features.Content.Services.ContentResolvers;
using GenHub.Features.GitHub.Services;
using GenHub.Features.Manifest;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods for registering content delivery services.
/// </summary>
public static class ContentPipelineModule
{
    /// <summary>
    /// Registers content delivery services for dependency injection.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddContentPipelineServices(this IServiceCollection services)
    {
        // Register core storage and manifest services
        services.AddSingleton<IContentStorageService, ContentStorageService>();
        services.AddScoped<IContentManifestPool, ContentManifestPool>();

        // Register core orchestrator
        services.AddSingleton<IContentOrchestrator, ContentOrchestrator>();

        // Register cache
        services.AddSingleton<IDynamicContentCache, MemoryDynamicContentCache>();

        // Register GitHub API client
        services.AddSingleton<IGitHubApiClient, OctokitGitHubApiClient>();

        // Register concrete content providers only
        services.AddTransient<IContentProvider, GitHubContentProvider>();
        services.AddTransient<IContentProvider, CNCLabsContentProvider>();
        services.AddTransient<IContentProvider, ModDBContentProvider>();
        services.AddTransient<IContentProvider, LocalFileSystemContentProvider>();

        // Register content discoverers
        services.AddTransient<IContentDiscoverer, GitHubDiscoverer>();
        services.AddTransient<IContentDiscoverer, GitHubReleasesDiscoverer>();
        services.AddTransient<IContentDiscoverer, CNCLabsMapDiscoverer>();
        services.AddTransient<IContentDiscoverer, FileSystemDiscoverer>();

        // Register content resolvers
        services.AddTransient<IContentResolver, GitHubResolver>();
        services.AddTransient<IContentResolver, CNCLabsMapResolver>();
        services.AddTransient<IContentResolver, LocalManifestResolver>();

        // Register content deliverers
        services.AddTransient<IContentDeliverer, HttpContentDeliverer>();
        services.AddTransient<IContentDeliverer, FileSystemDeliverer>();

        return services;
    }
}
