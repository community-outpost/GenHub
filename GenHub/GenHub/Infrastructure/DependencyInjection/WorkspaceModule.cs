using GenHub.Core.Interfaces.Workspace;
using GenHub.Features.Workspace;
using GenHub.Features.Workspace.Strategies;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for workspace services.
/// </summary>
public static class WorkspaceModule
{
    /// <summary>
    /// Registers workspace services with the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddWorkspaceServices(this IServiceCollection services)
    {
        // Core workspace services
        services.AddSingleton<IWorkspaceManager, WorkspaceManager>();
        services.AddSingleton<IWorkspaceValidator, WorkspaceValidator>();
        services.AddTransient<IFileOperationsService, FileOperationsService>();

        // Workspace strategies
        services.AddTransient<IWorkspaceStrategy, FullCopyStrategy>();
        services.AddTransient<IWorkspaceStrategy, SymlinkOnlyStrategy>();
        services.AddTransient<IWorkspaceStrategy, HardLinkStrategy>();
        services.AddTransient<IWorkspaceStrategy, HybridCopySymlinkStrategy>();

        // HttpClient for FileOperationsService
        services.AddHttpClient<FileOperationsService>();

        return services;
    }
}
