using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security;
using System.Text;
using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
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
    private const string InspectDesktopEntriesFailureMessage = "Failed to inspect desktop entries for custom installation location";

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
    public override string? GetRegisteredCustomInstallPath() => GetRegisteredCustomInstallPathStatic(logger);

    private static string? ResolveFromDesktopEntries(ILogger? logger)
    {
        try
        {
            var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrWhiteSpace(dataHome))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrWhiteSpace(home))
                {
                    return null;
                }

                dataHome = Path.Combine(home, ".local", "share");
            }

            var appDir = Path.Combine(dataHome, "applications");
            if (!Directory.Exists(appDir))
            {
                return null;
            }

            var candidateFiles = new[]
            {
                Path.Combine(appDir, $"{AppConstants.AppName}.desktop"),
                Path.Combine(appDir, $"community-outpost.{AppConstants.AppName}.desktop"),
            };

            foreach (var candidateFile in candidateFiles.Where(File.Exists))
            {
                var customRoot = ResolveCustomRootFromDesktopFile(candidateFile, logger);
                if (customRoot != null)
                {
                    return customRoot;
                }
            }
        }
        catch (IOException ex)
        {
            logger?.LogWarning(ex, InspectDesktopEntriesFailureMessage);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger?.LogWarning(ex, InspectDesktopEntriesFailureMessage);
        }
        catch (SecurityException ex)
        {
            logger?.LogWarning(ex, InspectDesktopEntriesFailureMessage);
        }
        catch (ArgumentException ex)
        {
            logger?.LogWarning(ex, InspectDesktopEntriesFailureMessage);
        }

        return null;
    }

    private static string? ResolveCustomRootFromDesktopFile(string candidateFile, ILogger? logger)
    {
        var execPath = ExtractExecPathFromDesktopFile(candidateFile);
        if (string.IsNullOrWhiteSpace(execPath))
        {
            return null;
        }

        var dir = Path.GetDirectoryName(execPath);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            return null;
        }

        var velopackRoot = ResolveCandidateVelopackRoot(dir);
        if (!string.IsNullOrWhiteSpace(velopackRoot) &&
            StorageMigrationService.IsVelopackRoot(velopackRoot) &&
            !PathHelper.AreSamePath(velopackRoot, StorageMigrationService.GetDefaultInstallRoot()))
        {
            logger?.LogInformation("Found registered custom install from desktop entry {DesktopFile}: {VelopackRoot}", candidateFile, velopackRoot);
            return velopackRoot;
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
                var candidate = ExtractExecutableToken(trimmed["Exec=".Length..].Trim());
                if (!string.IsNullOrWhiteSpace(candidate) &&
                    PathHelper.TrySanitizeLocalPath(candidate, out var sanitizedPath) &&
                    !PathHelper.AreSamePath(sanitizedPath, StorageMigrationService.GetSourceRootDirectory()))
                {
                    return sanitizedPath;
                }
            }
        }

        return null;
    }

    private static string ExtractExecutableToken(string raw)
    {
        if (raw.StartsWith('\"'))
        {
            var closingQuote = -1;
            for (var i = 1; i < raw.Length; i++)
            {
                if (raw[i] == '\"' && raw[i - 1] != '\\')
                {
                    closingQuote = i;
                    break;
                }
            }

            var insideQuotes = closingQuote > 1 ? raw[1..closingQuote] : raw.Trim('\"');
            return DecodeFreedesktopEscapes(insideQuotes);
        }

        var firstSpace = raw.IndexOf(' ');
        var token = firstSpace > 0 ? raw[..firstSpace] : raw;
        return DecodeFreedesktopEscapes(token);
    }

    private static string DecodeFreedesktopEscapes(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var sb = new StringBuilder(value.Length);
        var i = 0;
        while (i < value.Length)
        {
            var ch = value[i];
            if (ch == '%' && i + 1 < value.Length && value[i + 1] == '%')
            {
                sb.Append('%');
                i += 2;
            }
            else if (ch == '\\' && i + 1 < value.Length)
            {
                var next = value[i + 1];
                switch (next)
                {
                    case '\"':
                    case '\\':
                    case '$':
                    case '`':
                        sb.Append(next);
                        i += 2;
                        break;
                    case 's':
                        sb.Append(' ');
                        i += 2;
                        break;
                    case 'n':
                        sb.Append('\n');
                        i += 2;
                        break;
                    case 't':
                        sb.Append('\t');
                        i += 2;
                        break;
                    case 'r':
                        sb.Append('\r');
                        i += 2;
                        break;
                    default:
                        sb.Append(ch);
                        i++;
                        break;
                }
            }
            else
            {
                sb.Append(ch);
                i++;
            }
        }

        return sb.ToString();
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
