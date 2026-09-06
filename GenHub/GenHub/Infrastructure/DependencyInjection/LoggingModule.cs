using System;
using System.IO;
using System.Text.Json;
using GenHub.Core.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace GenHub.Infrastructure.DependencyInjection;

/// <summary>
/// Provides logging configuration and bootstrap logger factory.
/// </summary>
public static class LoggingModule
{
    private static LoggingLevelSwitch? _levelSwitch;

    /// <summary>
    /// Adds logging configuration to the service collection.
    /// Reads EnableDetailedLogging from user settings file if available.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddLoggingModule(this IServiceCollection services)
    {
        var logPath = GetLogFilePath();
        var enableDetailedLogging = ReadEnableDetailedLoggingFromSettings();
        var logLevel = enableDetailedLogging ? LogEventLevel.Debug : LogEventLevel.Information;
        var minLogLevel = enableDetailedLogging ? LogLevel.Debug : LogLevel.Information;

        // Create a level switch for runtime log level changes
        _levelSwitch = new LoggingLevelSwitch(logLevel);

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddConsole();
            builder.AddDebug();

            var logger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(_levelSwitch)
                .WriteTo.File(logPath, shared: true)
                .CreateLogger();

            builder.AddSerilog(logger);
            builder.SetMinimumLevel(minLogLevel);
        });

        return services;
    }

    /// <summary>
    /// Changes the log level at runtime without requiring a restart.
    /// </summary>
    /// <param name="enableDebug">True to enable DEBUG logging, false for INFO level.</param>
    public static void SetLogLevel(bool enableDebug)
    {
        if (_levelSwitch != null)
        {
            _levelSwitch.MinimumLevel = enableDebug ? LogEventLevel.Debug : LogEventLevel.Information;
        }
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
                .WriteTo.File(logPath, restrictedToMinimumLevel: LogEventLevel.Debug, shared: true)
                .CreateLogger();

            builder.AddSerilog(logger);
            builder.SetMinimumLevel(LogLevel.Debug);
        });
    }

    private static bool ReadEnableDetailedLoggingFromSettings()
    {
        try
        {
            var settingsPath = GetSettingsFilePath();
            if (!File.Exists(settingsPath))
            {
                return false;
            }

            var json = File.ReadAllText(settingsPath);
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.TryGetProperty(nameof(Core.Models.Common.UserSettings.EnableDetailedLogging).ToCamelCase(), out var property))
            {
                return property.GetBoolean();
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static string GetSettingsFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppConstants.AppName,
            FileTypes.SettingsFileName);
    }

    private static string GetLogFilePath()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppConstants.AppName,
            DirectoryNames.Logs);

        Directory.CreateDirectory(logDir);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd");
        return Path.Combine(logDir, $"{AppConstants.AppName.ToLowerInvariant()}-{timestamp}.log");
    }

    private static string ToCamelCase(this string str)
    {
        if (string.IsNullOrEmpty(str) || char.IsLower(str[0]))
        {
            return str;
        }

        return char.ToLowerInvariant(str[0]) + str.Substring(1);
    }
}
