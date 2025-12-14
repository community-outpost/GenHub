using System;
using System.Net.Http;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GeneralsOnline;
using GenHub.Features.GeneralsOnline.Services;
using GenHub.Features.GeneralsOnline.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods for registering Generals Online services and ViewModels.
/// </summary>
public static class GeneralsOnlineModule
{
    /// <summary>
    /// Registers Generals Online services and ViewModels for dependency injection.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddGeneralsOnlineServices(this IServiceCollection services)
    {
        // Register credentials storage service first (no dependencies)
        services.AddSingleton<ICredentialsStorageService, CredentialsStorageService>();

        // Register HTTP client for Generals Online API as singleton
        // Note: Uses token provider pattern to avoid circular dependency with auth service
        services.AddHttpClient<GeneralsOnlineApiClient>((serviceProvider, client) =>
        {
            client.BaseAddress = new Uri(GeneralsOnlineConstants.ApiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add(GeneralsOnlineConstants.AcceptHeader, GeneralsOnlineConstants.AcceptHeaderValue);
        });

        // Register the API client instance that will be shared and configured
        services.AddSingleton<IGeneralsOnlineApiClient>(serviceProvider =>
        {
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(GeneralsOnlineApiClient));
            var logger = serviceProvider.GetRequiredService<ILogger<GeneralsOnlineApiClient>>();

            return new GeneralsOnlineApiClient(httpClient, logger);
        });

        // Register authentication service as singleton (monitors credentials file)
        // This service depends on IGeneralsOnlineApiClient for login/verification calls
        services.AddSingleton<IGeneralsOnlineAuthService>(serviceProvider =>
        {
            var credentialsStorage = serviceProvider.GetRequiredService<ICredentialsStorageService>();
            var apiClient = serviceProvider.GetRequiredService<IGeneralsOnlineApiClient>();
            var logger = serviceProvider.GetRequiredService<ILogger<GeneralsOnlineAuthService>>();

            var authService = new GeneralsOnlineAuthService(credentialsStorage, apiClient, logger);

            // Configure the API client's token provider to use the auth service
            // This breaks the circular dependency by using deferred initialization
            apiClient.SetTokenProvider(() => authService.GetAuthTokenAsync());

            // Note: InitializeAsync must be called manually after service resolution
            // to avoid blocking the DI container construction

            return authService;
        });

        // Register HTML parsing service for scraping website data
        services.AddSingleton<HtmlParsingService>();

        // Register child ViewModels
        services.AddSingleton<LoginViewModel>();
        services.AddSingleton<LeaderboardViewModel>();
        services.AddSingleton<MatchHistoryViewModel>();
        services.AddSingleton<LobbiesViewModel>();
        services.AddSingleton<ServiceStatusViewModel>();

        // Register main ViewModel (depends on child ViewModels)
        services.AddSingleton<GeneralsOnlineViewModel>();

        return services;
    }
}
