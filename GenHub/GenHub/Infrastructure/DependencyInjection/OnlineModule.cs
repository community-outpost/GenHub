using GenHub.Core.Interfaces.Online;
using GenHub.Features.Online.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Infrastructure module for the Online (virtual LAN) feature.
/// Platform hosts replace <see cref="IVirtualLanAdapter"/> with their own bring-up.
/// </summary>
public static class OnlineModule
{
    /// <summary>
    /// Registers the Online feature services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOnlineServices(this IServiceCollection services)
    {
        services.AddHttpClient(nameof(OnlineNetworkService));
        services.AddSingleton<IOnlineNetworkService, OnlineNetworkService>();
        services.AddSingleton<IOnlinePresenceService, OnlinePresenceService>();
        services.AddSingleton<IOnlineLaunchService, OnlineLaunchService>();
        services.AddSingleton<IOverlaySidecarHost, OverlaySidecarHost>();
        services.AddSingleton<IP2PConnectionService, P2PConnectionService>();
        services.AddSingleton<IVirtualLanAdapter, NullVirtualLanAdapter>();
        return services;
    }
}
