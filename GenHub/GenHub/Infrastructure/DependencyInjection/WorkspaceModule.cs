using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Features.Workspace;
using GenHub.Features.Workspace.Strategies;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for workspace-related services.
/// </summary>
public static class WorkspaceModule
{
    /// <summary>
    /// Registers workspace services with the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddWorkspaceServices(this IServiceCollection services)
    {
        // Register file operations service with download service dependency
        services.AddScoped<IFileOperationsService, FileOperationsService>();

        // Register workspace manager and validator
        services.AddScoped<IWorkspaceManager, WorkspaceManager>();
        services.AddScoped<IWorkspaceValidator, WorkspaceValidator>();

        // Register workspace strategies as IWorkspaceStrategy
        services.AddScoped<IWorkspaceStrategy, FullCopyStrategy>();
        services.AddScoped<IWorkspaceStrategy, HardLinkStrategy>();
        services.AddScoped<IWorkspaceStrategy, HybridCopySymlinkStrategy>();
        services.AddScoped<IWorkspaceStrategy, SymlinkOnlyStrategy>();

        // Also register concrete types for direct injection if needed
        services.AddScoped<FullCopyStrategy>();
        services.AddScoped<HardLinkStrategy>();
        services.AddScoped<HybridCopySymlinkStrategy>();
        services.AddScoped<SymlinkOnlyStrategy>();

        return services;
    }
}
