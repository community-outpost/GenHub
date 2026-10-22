using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Features.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Shared Unix (Linux/macOS) service registrations.
/// </summary>
public static class UnixServicesModule
{
    /// <summary>
    /// Registers real hard-link file operations via link(2).
    /// Without this the base implementation throws, which is deliberate:
    /// silently copying made a missing registration invisible while
    /// every workspace consumed a full copy of the game.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddUnixFileOperations(this IServiceCollection services)
    {
        services.AddScoped<IFileOperationsService>(serviceProvider =>
        {
            var baseService = serviceProvider.GetRequiredService<FileOperationsService>();
            var casService = serviceProvider.GetRequiredService<ICasService>();
            var logger = serviceProvider.GetRequiredService<ILogger<UnixFileOperationsService>>();
            return new UnixFileOperationsService(baseService, casService, logger);
        });

        return services;
    }
}
