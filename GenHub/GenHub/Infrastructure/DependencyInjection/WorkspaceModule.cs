using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Storage;
using GenHub.Features.Storage.Services;
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
        // Register workspace strategies
        services.AddTransient<IWorkspaceStrategy, FullCopyStrategy>();
        services.AddTransient<IWorkspaceStrategy, SymlinkOnlyStrategy>();
        services.AddTransient<IWorkspaceStrategy, HybridCopySymlinkStrategy>();
        services.AddTransient<IWorkspaceStrategy, HardLinkStrategy>();

        // Register workspace manager with proper dependencies
        services.AddScoped<IWorkspaceManager, WorkspaceManager>();

        // Register workspace validator
        services.AddScoped<IWorkspaceValidator, WorkspaceValidator>();

        return services;
    }
}
