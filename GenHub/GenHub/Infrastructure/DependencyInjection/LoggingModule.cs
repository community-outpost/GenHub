using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Infrastructure.Logging;
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
    private static string? _activeLogFilePath;

    /// <summary>
    /// Gets or sets the active log file path for the running application instance.
    /// </summary>
    public static string ActiveLogFilePath
    {
        get => _activeLogFilePath ??= GetLogFilePath();
        set => _activeLogFilePath = value;
    }

    /// <summary>
    /// Adds logging configuration to the service collection.
    /// Reads EnableDetailedLogging from user settings file if available.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddLoggingModule(this IServiceCollection services)
    {
        var logPath = ActiveLogFilePath;
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
                .WriteTo.Sink(new ResilientFileSink(logPath))
                .CreateLogger();

            builder.AddSerilog(logger, dispose: true);
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
        var logPath = ActiveLogFilePath;

        return LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();

            var logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Sink(new ResilientFileSink(logPath), restrictedToMinimumLevel: LogEventLevel.Debug)
                .CreateLogger();

            builder.AddSerilog(logger, dispose: true);
            builder.SetMinimumLevel(LogLevel.Debug);
        });
    }

    /// <summary>
    /// Gets the path to the current day's log file.
    /// </summary>
    /// <returns>The full path to the log file.</returns>
    public static string GetLogFilePath()
    {
        try
        {
            string rootDir;
            if (StorageMigrationService.IsCustomInstallRoot())
            {
                rootDir = StorageMigrationService.GetSourceRootDirectory();
                try
                {
                    StorageMigrationService.CleanOrphanedDefaultAppDataIfCustom();
                }
                catch (IOException)
                {
                    // Non-fatal cleanup
                }
                catch (UnauthorizedAccessException)
                {
                    // Non-fatal cleanup
                }
                catch (System.Security.SecurityException)
                {
                    // Non-fatal cleanup
                }
                catch (ArgumentException)
                {
                    // Non-fatal cleanup
                }
            }
            else
            {
                rootDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    AppConstants.AppName);
            }

            var logDir = Path.Combine(rootDir, DirectoryNames.Logs);
            Directory.CreateDirectory(logDir);
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return Path.Combine(logDir, $"{AppConstants.AppName.ToLowerInvariant()}-{timestamp}.log");
        }
        catch (IOException)
        {
            return GetFallbackLogFilePath();
        }
        catch (UnauthorizedAccessException)
        {
            return GetFallbackLogFilePath();
        }
        catch (System.Security.SecurityException)
        {
            return GetFallbackLogFilePath();
        }
        catch (ArgumentException)
        {
            return GetFallbackLogFilePath();
        }
    }

    private static string GetFallbackLogFilePath()
    {
        try
        {
            var fallbackDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppConstants.AppName,
                DirectoryNames.Logs);
            Directory.CreateDirectory(fallbackDir);
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return Path.Combine(fallbackDir, $"{AppConstants.AppName.ToLowerInvariant()}-{timestamp}.log");
        }
        catch (IOException)
        {
            return GetTempLogFilePath();
        }
        catch (UnauthorizedAccessException)
        {
            return GetTempLogFilePath();
        }
        catch (System.Security.SecurityException)
        {
            return GetTempLogFilePath();
        }
        catch (ArgumentException)
        {
            return GetTempLogFilePath();
        }
    }

    private static string GetTempLogFilePath()
    {
        try
        {
            var tempLogDir = Path.Combine(Path.GetTempPath(), AppConstants.AppName, DirectoryNames.Logs);
            Directory.CreateDirectory(tempLogDir);
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return Path.Combine(tempLogDir, $"{AppConstants.AppName.ToLowerInvariant()}-{timestamp}.log");
        }
        catch (IOException)
        {
            return Path.Combine(Path.GetTempPath(), $"{AppConstants.AppName.ToLowerInvariant()}.log");
        }
        catch (UnauthorizedAccessException)
        {
            return Path.Combine(Path.GetTempPath(), $"{AppConstants.AppName.ToLowerInvariant()}.log");
        }
        catch (System.Security.SecurityException)
        {
            return Path.Combine(Path.GetTempPath(), $"{AppConstants.AppName.ToLowerInvariant()}.log");
        }
        catch (ArgumentException)
        {
            return Path.Combine(Path.GetTempPath(), $"{AppConstants.AppName.ToLowerInvariant()}.log");
        }
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
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }

    private static string GetSettingsFilePath()
    {
        try
        {
            var rootDir = StorageMigrationService.IsCustomInstallRoot()
                ? StorageMigrationService.GetSourceRootDirectory()
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    AppConstants.AppName);

            return Path.Combine(rootDir, FileTypes.SettingsFileName);
        }
        catch (IOException)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppConstants.AppName,
                FileTypes.SettingsFileName);
        }
        catch (UnauthorizedAccessException)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppConstants.AppName,
                FileTypes.SettingsFileName);
        }
        catch (System.Security.SecurityException)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppConstants.AppName,
                FileTypes.SettingsFileName);
        }
        catch (ArgumentException)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppConstants.AppName,
                FileTypes.SettingsFileName);
        }
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
