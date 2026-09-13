using System;
using System.IO;
using System.Security;
using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace GenHub.Windows.Features.Storage;

/// <summary>
/// Tracks Windows registry installation markers and detects duplicate or orphaned installations.
/// </summary>
/// <param name="logger">Optional logger for diagnostics.</param>
public sealed class WindowsInstallationTracker(ILogger<WindowsInstallationTracker>? logger = null) : IInstallationLocationTracker
{
    private const string GenHubSubKey = RegistryConstants.GenHubSubKey;
    private const string CustomInstallPathValueName = RegistryConstants.CustomInstallPathValueName;
    private const string UriSchemeCommandKey = RegistryConstants.GenHubUriSchemeCommandKey;
    private const string RecordLocationFailureMessage = "Failed to record installation location in registry.";
    private const string ReadLocationFailureMessage = "Failed to read registered custom installation location from registry.";
    private const string ClearLocationFailureMessage = "Failed to clear custom installation path from registry.";

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

        if (StorageMigrationService.IsCustomInstallRoot())
        {
            try
            {
                var customRoot = StorageMigrationService.GetSourceRootDirectory();
                using var key = Registry.CurrentUser.CreateSubKey(GenHubSubKey, writable: true);
                key.SetValue(CustomInstallPathValueName, customRoot);
                logger?.LogInformation("Recorded custom installation root in registry: {CustomRoot}", customRoot);
            }
            catch (SecurityException ex)
            {
                logger?.LogWarning(ex, RecordLocationFailureMessage);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger?.LogWarning(ex, RecordLocationFailureMessage);
            }
            catch (IOException ex)
            {
                logger?.LogWarning(ex, RecordLocationFailureMessage);
            }
            catch (ArgumentException ex)
            {
                logger?.LogWarning(ex, RecordLocationFailureMessage);
            }
            catch (InvalidOperationException ex)
            {
                logger?.LogWarning(ex, RecordLocationFailureMessage);
            }

            FileInstallationLocationTracker.RecordInstallLocationStatic(logger);
        }
    }

    /// <summary>
    /// Retrieves the registered custom installation directory from registry, with fallback to URI scheme command and file tracker.
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
            var pathFromKey = GetPathFromGenHubRegistryKey();
            if (pathFromKey != null)
            {
                return pathFromKey;
            }

            // 2. Fallback: inspect URI scheme handler command to see where genhub:// previously pointed
            var pathFromUriScheme = GetPathFromUriSchemeRegistration();
            if (pathFromUriScheme != null)
            {
                return pathFromUriScheme;
            }

            // 3. Fallback: check file installation tracker
            var fromFile = FileInstallationLocationTracker.GetRegisteredCustomInstallPathStatic(logger);
            if (!string.IsNullOrWhiteSpace(fromFile))
            {
                return fromFile;
            }
        }
        catch (SecurityException ex)
        {
            logger?.LogWarning(ex, ReadLocationFailureMessage);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger?.LogWarning(ex, ReadLocationFailureMessage);
        }
        catch (IOException ex)
        {
            logger?.LogWarning(ex, ReadLocationFailureMessage);
        }
        catch (ArgumentException ex)
        {
            logger?.LogWarning(ex, ReadLocationFailureMessage);
        }
        catch (InvalidOperationException ex)
        {
            logger?.LogWarning(ex, ReadLocationFailureMessage);
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
        catch (SecurityException ex)
        {
            logger?.LogWarning(ex, ClearLocationFailureMessage);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger?.LogWarning(ex, ClearLocationFailureMessage);
        }
        catch (IOException ex)
        {
            logger?.LogWarning(ex, ClearLocationFailureMessage);
        }
        catch (ArgumentException ex)
        {
            logger?.LogWarning(ex, ClearLocationFailureMessage);
        }
        catch (InvalidOperationException ex)
        {
            logger?.LogWarning(ex, ClearLocationFailureMessage);
        }

        FileInstallationLocationTracker.ClearCustomInstallPathStatic(logger);
    }

    /// <inheritdoc />
    public void RecordInstallLocation() => RecordInstallLocationStatic(logger);

    /// <inheritdoc />
    public string? GetRegisteredCustomInstallPath() => GetRegisteredCustomInstallPathStatic(logger);

    /// <inheritdoc />
    public void ClearCustomInstallPath() => ClearCustomInstallPathStatic(logger);

    private static string? GetPathFromGenHubRegistryKey()
    {
        using var key = Registry.CurrentUser.OpenSubKey(GenHubSubKey, writable: false);
        if (key != null)
        {
            var customPath = key.GetValue(CustomInstallPathValueName) as string;
            if (IsValidLocalDirectoryPath(customPath))
            {
                return customPath!.Trim().Trim('"');
            }
        }

        return null;
    }

    private static string? GetPathFromUriSchemeRegistration()
    {
        using var uriCommandKey = Registry.CurrentUser.OpenSubKey(UriSchemeCommandKey, writable: false);
        if (uriCommandKey != null)
        {
            var command = uriCommandKey.GetValue(string.Empty) as string;
            if (!string.IsNullOrWhiteSpace(command))
            {
                var candidate = ExtractDirectoryFromCommand(command);
                if (IsValidLocalDirectoryPath(candidate) &&
                    StorageMigrationService.IsVelopackRoot(candidate!))
                {
                    return candidate!.Trim().Trim('"');
                }
            }
        }

        return null;
    }

    private static bool IsValidLocalDirectoryPath(string? path)
    {
        return PathHelper.TrySanitizeLocalPath(path, out var sanitized) && Directory.Exists(sanitized);
    }

    private static string? ExtractDirectoryFromCommand(string command)
    {
        // Format typically: "C:\path\to\GenHub.Windows.exe" "%1"
        var trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            var endQuote = trimmed.IndexOf('"', 1);
            if (endQuote > 1)
            {
                var exePath = trimmed[1..endQuote];
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
}
