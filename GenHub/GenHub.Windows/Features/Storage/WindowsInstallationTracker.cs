using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security;

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
            var customRoot = StorageMigrationService.GetSourceRootDirectory();
            RecordCustomInstallPathStatic(customRoot, logger);
        }
    }

    /// <summary>
    /// Records an explicitly specified custom installation directory in the user registry and file tracker.
    /// </summary>
    /// <param name="customPath">The custom installation root directory path to record.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public static void RecordCustomInstallPathStatic(string customPath, ILogger? logger = null)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(customPath))
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(GenHubSubKey, writable: true);
            key.SetValue(CustomInstallPathValueName, customPath);
            logger?.LogInformation("Recorded custom installation root in registry: {CustomRoot}", customPath);
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

        FileInstallationLocationTracker.RecordCustomInstallPathStatic(customPath, logger);
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

        // 1. Direct GenHub registry key
        var pathFromKey = GetPathFromGenHubRegistryKey(logger);
        if (pathFromKey != null)
        {
            return pathFromKey;
        }

        // 2. Fallback: inspect URI scheme handler command to see where genhub:// previously pointed
        var pathFromUriScheme = GetPathFromUriSchemeRegistration(logger);
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

    private static string? GetPathFromGenHubRegistryKey(ILogger? logger = null)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(GenHubSubKey, writable: false);
            if (key != null)
            {
                var customPath = key.GetValue(CustomInstallPathValueName) as string;
                if (TryGetValidLocalDirectoryPath(customPath, out var sanitized) &&
                    IsValidCustomInstallCandidate(sanitized))
                {
                    return sanitized;
                }
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException or ArgumentException or InvalidOperationException)
        {
            logger?.LogWarning(ex, ReadLocationFailureMessage);
        }

        return null;
    }

    private static string? GetPathFromUriSchemeRegistration(ILogger? logger = null)
    {
        try
        {
            using var uriCommandKey = Registry.CurrentUser.OpenSubKey(UriSchemeCommandKey, writable: false);
            if (uriCommandKey != null)
            {
                var command = uriCommandKey.GetValue(string.Empty) as string;
                if (!string.IsNullOrWhiteSpace(command))
                {
                    var candidate = ExtractDirectoryFromCommand(command);
                    if (TryGetValidLocalDirectoryPath(candidate, out var sanitized) &&
                        IsValidCustomInstallCandidate(sanitized))
                    {
                        return sanitized;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException or ArgumentException or InvalidOperationException)
        {
            logger?.LogWarning(ex, ReadLocationFailureMessage);
        }

        return null;
    }

    private static bool IsValidCustomInstallCandidate(string path)
    {
        if (!StorageMigrationService.IsVelopackRoot(path))
        {
            return false;
        }

        var currentRoot = StorageMigrationService.GetSourceRootDirectory();
        if (PathHelper.AreSamePath(path, currentRoot))
        {
            return false;
        }

        var defaultRoot = StorageMigrationService.GetDefaultInstallRoot();
        if (!string.IsNullOrWhiteSpace(defaultRoot))
        {
            if (PathHelper.AreSamePath(path, defaultRoot))
            {
                return false;
            }

            var parent = Directory.GetParent(path)?.FullName;
            if (parent != null && PathHelper.AreSamePath(parent, defaultRoot))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetValidLocalDirectoryPath(string? path, [NotNullWhen(true)] out string? sanitized)
    {
        if (PathHelper.TrySanitizeLocalPath(path, out sanitized) && Directory.Exists(sanitized))
        {
            return true;
        }

        sanitized = null;
        return false;
    }

    private static string? ExtractDirectoryFromCommand(string command)
    {
        // Format typically: "C:\path\to\GenHub.Windows.exe" "%1"
        var trimmed = command.Trim();
        if (trimmed.StartsWith('\"'))
        {
            var endQuote = trimmed.IndexOf('\"', 1);
            if (endQuote > 1)
            {
                var exePath = trimmed[1..endQuote];
                var dir = Path.GetDirectoryName(exePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    return ResolveCandidateVelopackRoot(dir);
                }
            }
        }

        return null;
    }

    private static string? ResolveCandidateVelopackRoot(string directory)
    {
        if (StorageMigrationService.IsVelopackRoot(directory))
        {
            return directory;
        }

        var parent = Directory.GetParent(directory)?.FullName;
        if (parent != null && StorageMigrationService.IsVelopackRoot(parent))
        {
            return parent;
        }

        return directory;
    }
}
