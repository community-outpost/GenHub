using System;
using GenHub.Common.Services;
using GenHub.Core.Interfaces.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Dependency injection module for configuration services.
/// </summary>
public static class ConfigurationModule
{
    /// <summary>
    /// Registers configuration services with the service collection and returns the configuration provider.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <returns>The configuration provider service instance.</returns>
    public static IConfigurationProviderService AddConfigurationModule(this IServiceCollection services)
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

        services.AddSingleton<IAppConfiguration, AppConfiguration>();
        services.AddSingleton<IUserSettingsService, UserSettingsService>();
        services.AddSingleton<IConfigurationProviderService, ConfigurationProviderService>();

        // Build temporary provider to get configuration service for other modules
        using var tempProvider = services.BuildServiceProvider();
        return tempProvider.GetRequiredService<IConfigurationProviderService>();
    }
}
