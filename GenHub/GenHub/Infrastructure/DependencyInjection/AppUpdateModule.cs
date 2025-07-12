using GenHub.Core.Interfaces.AppUpdate;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Features.AppUpdate.Services;
using GenHub.Features.GitHub.Services;
using Microsoft.Extensions.DependencyInjection;
using Octokit;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods for registering App Update module dependencies.
/// </summary>
public static class AppUpdateModule
{
    /// <summary>
    /// Registers the App Update module dependencies in the service collection.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> with App Update module services registered.</returns>
    public static IServiceCollection AddAppUpdateModule(this IServiceCollection services)
    {
        services.AddSingleton<IAppVersionService, AppVersionService>();
        services.AddSingleton<IVersionComparator, SemVerComparator>();
        services.AddSingleton<IGitHubApiClient, OctokitGitHubApiClient>();

        services.AddSingleton<IAppUpdateService, AppUpdateService>();

        services.AddSingleton(new GitHubClient(new ProductHeaderValue("GenHub")));

        return services;
    }
}
