using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

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
        var logPath = GetLogFilePath();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddConsole();
            builder.AddDebug();

            var logger = new LoggerConfiguration()
                .WriteTo.File(logPath, restrictedToMinimumLevel: LogEventLevel.Information)
                .CreateLogger();

            builder.AddSerilog(logger);
            builder.SetMinimumLevel(LogLevel.Information);
        });

        return services;
    }

    /// <summary>
    /// Creates a bootstrap logger factory for early logging.
    /// </summary>
    /// <returns>An <see cref="ILoggerFactory"/> instance.</returns>
    public static ILoggerFactory CreateBootstrapLoggerFactory()
    {
        var logPath = GetLogFilePath();

        return LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();

            var logger = new LoggerConfiguration()
                .WriteTo.File(logPath, restrictedToMinimumLevel: LogEventLevel.Debug)
                .CreateLogger();

            builder.AddSerilog(logger);
            builder.SetMinimumLevel(LogLevel.Debug);
        });
    }

    private static string GetLogFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var logDir = Path.Combine(appData, "GenHub", "logs");
        Directory.CreateDirectory(logDir);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd");
        return Path.Combine(logDir, $"genhub-{timestamp}.log");
    }
}
