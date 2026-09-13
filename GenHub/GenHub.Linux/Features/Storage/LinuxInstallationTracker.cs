using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security;
using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Storage;
using Microsoft.Extensions.Logging;

namespace GenHub.Linux.Features.Storage;

/// <summary>
/// Linux implementation of <see cref="IInstallationLocationTracker"/> that persists the custom installation root
/// to user profile state and inspects desktop entry shortcuts as fallback.
/// </summary>
/// <param name="logger">Optional logger for diagnostics.</param>
[SupportedOSPlatform("linux")]
public class LinuxInstallationTracker(ILogger<LinuxInstallationTracker>? logger = null)
    : FileInstallationLocationTracker(logger), IInstallationLocationTracker
{
    private readonly ILogger<LinuxInstallationTracker>? _logger = logger;

    /// <summary>
    /// Records the current installation directory if running from a custom install root.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public static new void RecordInstallLocationStatic(ILogger? logger = null)
    {
        FileInstallationLocationTracker.RecordInstallLocationStatic(logger);
    }

    /// <summary>
    /// Gets the registered custom install path with fallback to Linux .desktop files.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <returns>The registered custom installation path if found; otherwise, <see langword="null"/>.</returns>
    public static new string? GetRegisteredCustomInstallPathStatic(ILogger? logger = null)
    {
        var pathFromFile = FileInstallationLocationTracker.GetRegisteredCustomInstallPathStatic(logger);
        if (!string.IsNullOrWhiteSpace(pathFromFile))
        {
            return pathFromFile;
        }

        return ResolveFromDesktopEntries(logger);
    }

    /// <inheritdoc />
    public override string? GetRegisteredCustomInstallPath() => GetRegisteredCustomInstallPathStatic(_logger);

    private static string? ResolveFromDesktopEntries(ILogger? logger)
    {
        try
        {
            var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrWhiteSpace(dataHome))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                dataHome = Path.Combine(home, ".local", "share");
            }

            var appDir = Path.Combine(dataHome, "applications");
            var candidateFiles = new[]
            {
                Path.Combine(appDir, $"{AppConstants.AppName}.desktop"),
                Path.Combine(appDir, $"community-outpost.{AppConstants.AppName}.desktop"),
            };

            foreach (var candidateFile in candidateFiles.Where(File.Exists))
            {
                var execPath = ExtractExecPathFromDesktopFile(candidateFile);
                if (!string.IsNullOrWhiteSpace(execPath))
                {
                    var dir = Path.GetDirectoryName(execPath);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    {
                        var velopackRoot = ResolveCandidateVelopackRoot(dir);
                        if (!string.IsNullOrWhiteSpace(velopackRoot) &&
                            StorageMigrationService.IsVelopackRoot(velopackRoot) &&
                            !string.Equals(velopackRoot, StorageMigrationService.GetDefaultInstallRoot(), StringComparison.OrdinalIgnoreCase))
                        {
                            logger?.LogInformation("Found registered custom install from desktop entry {DesktopFile}: {VelopackRoot}", candidateFile, velopackRoot);
                            return velopackRoot;
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            logger?.LogWarning(ex, "Failed to inspect desktop entries for custom installation location");
        }

        return null;
    }

    private static string? ExtractExecPathFromDesktopFile(string desktopFilePath)
    {
        foreach (var line in File.ReadLines(desktopFilePath))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Exec=", StringComparison.OrdinalIgnoreCase))
            {
                var raw = trimmed["Exec=".Length..].Trim();
                if (raw.StartsWith('"'))
                {
                    var closingQuote = raw.IndexOf('"', 1);
                    return closingQuote > 1 ? raw[1..closingQuote] : raw.Trim('"');
                }

                var firstSpace = raw.IndexOf(' ');
                return firstSpace > 0 ? raw[..firstSpace] : raw;
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

        return null;
    }
}
