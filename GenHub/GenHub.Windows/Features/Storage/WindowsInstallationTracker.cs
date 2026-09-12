using System;
using System.IO;
using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace GenHub.Windows.Features.Storage;

/// <summary>
/// Tracks Windows registry installation markers and detects duplicate or orphaned installations.
/// </summary>
public sealed class WindowsInstallationTracker : IInstallationLocationTracker
{
    private const string GenHubSubKey = RegistryConstants.GenHubSubKey;
    private const string CustomInstallPathValueName = RegistryConstants.CustomInstallPathValueName;
    private const string UriSchemeCommandKey = @"Software\Classes\" + CommandLineConstants.SchemeName + @"\shell\open\command";

    private readonly ILogger<WindowsInstallationTracker>? _logger;

    /// <summary>
    /// Records the current installation directory in the user registry when running from a custom install root.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public static void RecordInstallLocationStatic(ILogger? logger = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            if (StorageMigrationService.IsCustomInstallRoot())
            {
                var customRoot = StorageMigrationService.GetSourceRootDirectory();
                using var key = Registry.CurrentUser.CreateSubKey(GenHubSubKey, writable: true);
                key.SetValue(CustomInstallPathValueName, customRoot);
                logger?.LogInformation("Recorded custom installation root in registry: {CustomRoot}", customRoot);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to record installation location in registry.");
        }
    }

    /// <summary>
    /// Retrieves the registered custom installation directory from registry, with fallback to URI scheme command.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <returns>The registered custom installation path if found; otherwise, <see langword="null"/>.</returns>
    public static string? GetRegisteredCustomInstallPathStatic(ILogger? logger = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            // 1. Direct GenHub registry key
            using (var key = Registry.CurrentUser.OpenSubKey(GenHubSubKey, writable: false))
            {
                if (key != null)
                {
                    var customPath = key.GetValue(CustomInstallPathValueName) as string;
                    if (!string.IsNullOrWhiteSpace(customPath) && Directory.Exists(customPath))
                    {
                        return customPath;
                    }
                }
            }

            // 2. Fallback: inspect URI scheme handler command to see where genhub:// previously pointed
            using (var uriCommandKey = Registry.CurrentUser.OpenSubKey(UriSchemeCommandKey, writable: false))
            {
                if (uriCommandKey != null)
                {
                    var command = uriCommandKey.GetValue(string.Empty) as string;
                    if (!string.IsNullOrWhiteSpace(command))
                    {
                        var candidate = ExtractDirectoryFromCommand(command);
                        if (!string.IsNullOrWhiteSpace(candidate) &&
                            Directory.Exists(candidate) &&
                            StorageMigrationService.IsVelopackRoot(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to read registered custom installation location from registry.");
        }

        return null;
    }

    /// <summary>
    /// Clears the registered custom installation path from registry (e.g. after migration to default location).
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public static void ClearCustomInstallPathStatic(ILogger? logger = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(GenHubSubKey, writable: true);
            key?.DeleteValue(CustomInstallPathValueName, throwOnMissingValue: false);
            logger?.LogInformation("Cleared custom installation path from registry.");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to clear custom installation path from registry.");
        }
    }

    private static string? ExtractDirectoryFromCommand(string command)
    {
        // Format typically: "C:\\path\\to\\GenHub.Windows.exe" "%1"
        var trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            var endQuote = trimmed.IndexOf('"', 1);
            if (endQuote > 1)
            {
                var exePath = trimmed.Substring(1, endQuote - 1);
                var dir = Path.GetDirectoryName(exePath);
                if (string.Equals(Path.GetFileName(dir), "current", StringComparison.OrdinalIgnoreCase))
                {
                    return Directory.GetParent(dir!)?.FullName;
                }

                return dir;
            }
        }

        return null;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsInstallationTracker"/> class.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public WindowsInstallationTracker(ILogger<WindowsInstallationTracker>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void RecordInstallLocation() => RecordInstallLocationStatic(_logger);

    /// <inheritdoc />
    public string? GetRegisteredCustomInstallPath() => GetRegisteredCustomInstallPathStatic(_logger);

    /// <inheritdoc />
    public void ClearCustomInstallPath() => ClearCustomInstallPathStatic(_logger);
}
