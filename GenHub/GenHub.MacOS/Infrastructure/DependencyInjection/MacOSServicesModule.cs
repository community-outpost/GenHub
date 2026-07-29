using System.Runtime.Versioning;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Features.AppUpdate.Interfaces;
using GenHub.Features.AppUpdate.Services;
using GenHub.MacOS.Features.Shortcuts;
using GenHub.MacOS.GameInstallations;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.MacOS.Infrastructure.DependencyInjection;

/// <summary>
/// Registers services implemented specifically for macOS.
/// </summary>
public static class MacOSServicesModule
{
    /// <summary>
    /// Registers macOS platform services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    [SupportedOSPlatform("macos")]
    public static IServiceCollection AddMacOSServices(this IServiceCollection services)
    {
        services.AddSingleton<IGameInstallationDetector, MacOSInstallationDetector>();
        services.AddSingleton<IShortcutService, MacOSShortcutService>();

        // Disables self-update on macOS, which publishes no update artifacts.
        // AppServices.ConfigureApplicationServices invokes the platform module after
        // AddAppUpdateModule, so this registration supersedes VelopackUpdateManager.
        // Delete this line once macOS artifacts are published.
        services.AddSingleton<IVelopackUpdateManager, UnsupportedPlatformUpdateManager>();

        return services;
    }
}
