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
    private static IConfiguration? _cachedConfiguration;

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
    /// <returns>The configured <see cref="IConfiguration"/> instance.</returns>
    public static IConfiguration InitializeConfiguredDataPathResolver(IConfiguration? configuration = null)
    {
        var config = configuration ?? _cachedConfiguration ?? (_cachedConfiguration = CreateConfiguration());
        _cachedConfiguration = config;
        StorageMigrationService.SetConfiguredDataPathResolver(() => config[ConfigurationKeys.AppDataPath]);
        return config;
    }

    /// <summary>
    /// Resets the cached configuration instance (for unit testing).
    /// </summary>
    internal static void ResetCachedConfigurationForTesting()
    {
        _cachedConfiguration = null;
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

        // Initialize configured data-path resolver eagerly and register configuration instance
        var config = InitializeConfiguredDataPathResolver();
        services.AddSingleton<IConfiguration>(config);

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
            var logger = provider.GetService<ILogger<AppConfiguration>>();
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
            var configProvider = provider.GetRequiredService<IConfigurationProviderService>();
            var logger = provider.GetService<ILogger<ImageCacheService>>();
            return new ImageCacheService(configProvider, logger);
        });

        return services;
    }
}
