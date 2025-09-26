using System;
using GenHub.Core.Interfaces.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Provides logging configuration and bootstrap logger factory.
/// </summary>
public static class LoggingModule
{
    /// <summary>
    /// Adds logging configuration to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddLoggingModule(this IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddConsole();
            builder.AddDebug();
        });

        services.AddSingleton<IConfigureOptions<LoggerFilterOptions>>(serviceProvider =>
            new ConfigureNamedOptions<LoggerFilterOptions>(null, options =>
            {
                var configProvider = serviceProvider.GetRequiredService<IConfigurationProviderService>();
                var minLevel = configProvider.GetEnableDetailedLogging() ? LogLevel.Debug : LogLevel.Information;
                options.MinLevel = minLevel;
            }));

        return services;
    }

    /// <summary>
    /// Creates a bootstrap logger factory for early logging.
    /// </summary>
    /// <returns>An <see cref="ILoggerFactory"/> instance.</returns>
    public static ILoggerFactory CreateBootstrapLoggerFactory() =>
        LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
}
