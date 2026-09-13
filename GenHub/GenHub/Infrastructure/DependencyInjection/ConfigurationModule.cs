using System;
using GenHub.Common.Services;
using GenHub.Core.Constants;
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
    /// Builds application configuration from appsettings files and environment variables.
    /// </summary>
    /// <returns>The constructed <see cref="IConfiguration"/> instance.</returns>
    public static IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables("GENHUB_")
            .Build();
    }

    /// <summary>
    /// Initializes the configured data-path resolver for storage services before early adoption executes.
    /// </summary>
    /// <param name="configuration">Optional pre-built configuration.</param>
    public static void InitializeConfiguredDataPathResolver(IConfiguration? configuration = null)
    {
        if (configuration != null)
        {
            StorageMigrationService.SetConfiguredDataPathResolver(() => configuration[ConfigurationKeys.AppDataPath]);
        }
        else
        {
            StorageMigrationService.SetConfiguredDataPathResolver(() => CreateConfiguration()[ConfigurationKeys.AppDataPath]);
        }
    }

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
            var config = CreateConfiguration();
            StorageMigrationService.SetConfiguredDataPathResolver(() => config[ConfigurationKeys.AppDataPath]);
            return config;
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
        services.AddSingleton<IAppConfiguration>(provider =>
        {
            var config = provider.GetService<IConfiguration>();
            var logger = provider.GetService<ILogger<AppConfiguration>>();
            if (config != null)
            {
                StorageMigrationService.SetConfiguredDataPathResolver(() => config[ConfigurationKeys.AppDataPath]);
            }

            return new AppConfiguration(config, logger);
        });
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
