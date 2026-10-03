using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.GeneralsOnline;
using GenHub.Core.Interfaces.Online;
using GenHub.Features.GeneralsOnline.Services;
using GenHub.Features.Online.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Infrastructure module for the Online feature and Generals Online multiplayer integration.
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
        services.AddSingleton<IOnlineLaunchService>(sp => new OnlineLaunchService(
            sp.GetRequiredService<IGameProfileManager>(),
            sp.GetRequiredService<IProfileLauncherFacade>(),
            sp.GetRequiredService<ILogger<OnlineLaunchService>>(),
            sp.GetService<IGameSettingsService>()));

        services.AddGeneralsOnlineServices();
        return services;
    }

    /// <summary>
    /// Registers the Generals Online lobby browser services.
    /// The shared encrypted-file token storage is the fallback; platform
    /// hosts replace <see cref="IGeneralsOnlineTokenStorage"/> with their
    /// DPAPI or platform-specific encrypted-file implementation.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGeneralsOnlineServices(this IServiceCollection services)
    {
        services.AddHttpClient(nameof(GeneralsOnlineApiClient));
        services.AddSingleton<IGeneralsOnlineApiClient>(serviceProvider =>
        {
            var client = new GeneralsOnlineApiClient(
                serviceProvider.GetRequiredService<IHttpClientFactory>(),
                serviceProvider.GetRequiredService<ILogger<GeneralsOnlineApiClient>>());
            client.SetTokenProvider(() => serviceProvider.GetRequiredService<IGeneralsOnlineAuthService>().GetSessionTokenAsync());
            return client;
        });
        services.AddSingleton<IGeneralsOnlineAuthService, GeneralsOnlineAuthService>();
        services.AddSingleton<IGeneralsOnlineCompatibilityService, GeneralsOnlineCompatibilityService>();
        services.AddSingleton<IGeneralsOnlineWebSocketListener, GeneralsOnlineWebSocketListener>();
        services.AddSingleton<IGeneralsOnlineTokenStorage, EncryptedFileGeneralsOnlineTokenStorage>();
        return services;
    }
}
