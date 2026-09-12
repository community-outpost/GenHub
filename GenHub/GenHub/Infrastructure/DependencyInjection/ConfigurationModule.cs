using System;
using GenHub.Common.Services;
using GenHub.Core.Interfaces.Common;
using GenHub.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for configuration services.
/// </summary>
public static class ConfigurationModule
{
    /// <summary>
    /// Registers configuration services with the service collection.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddConfigurationModule(this IServiceCollection services)
    {
        // Create bootstrap logger factory for configuration services
        var bootstrapLoggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning);
        });

        // Register IConfiguration first - this is required by AppConfiguration
        services.AddSingleton<IConfiguration>(provider =>
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
                .AddEnvironmentVariables("GENHUB_");

            return builder.Build();
        });

        // Register bootstrap loggers for configuration services
        services.AddSingleton<ILogger<AppConfiguration>>(provider =>
            bootstrapLoggerFactory.CreateLogger<AppConfiguration>());
        services.AddSingleton<ILogger<UserSettingsService>>(provider =>
            bootstrapLoggerFactory.CreateLogger<UserSettingsService>());
        services.AddSingleton<ILogger<ConfigurationProviderService>>(provider =>
            bootstrapLoggerFactory.CreateLogger<ConfigurationProviderService>());
        services.AddSingleton<ILogger<StorageLocationService>>(provider =>
            bootstrapLoggerFactory.CreateLogger<StorageLocationService>());
        services.AddSingleton<ILogger<ThemeService>>(provider =>
            bootstrapLoggerFactory.CreateLogger<ThemeService>());
        services.AddSingleton<ISessionPreferenceService, SessionPreferenceService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IAppConfiguration, AppConfiguration>();
        services.AddSingleton<IUserSettingsService, UserSettingsService>();
        services.AddSingleton<IConfigurationProviderService, ConfigurationProviderService>();
        services.TryAddSingleton<IStorageWritabilityProbe, StorageWritabilityProbe>();
        services.AddSingleton<IStorageLocationService, StorageLocationService>();
        services.AddSingleton<IThemeService, ThemeService>();

        // Register image cache service with resolved configuration provider and logger
        services.AddSingleton<IImageCacheService>(provider =>
        {
            var config = provider.GetRequiredService<IConfigurationProviderService>();
            var logger = provider.GetService<ILogger<ImageCacheService>>();
            return new ImageCacheService(config, logger);
        });

        return services;
    }
}
