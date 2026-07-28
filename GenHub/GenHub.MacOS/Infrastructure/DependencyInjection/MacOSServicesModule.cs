using System.Runtime.Versioning;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.MacOS.Features.Shortcuts;
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
        services.AddSingleton<IShortcutService, MacOSShortcutService>();

        return services;
    }
}
