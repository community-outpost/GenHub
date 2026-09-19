using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

namespace GenHub.Core.Extensions.GameInstallations;

/// <summary>
/// Extension methods for game installation types.
/// </summary>
public static class InstallationExtensions
{
    private static readonly HashSet<string> InstallationIdentifierSet = new(
        Enum.GetValues<GameInstallationType>().Select(t => t.ToIdentifierString())
            .Concat(new[] { PublisherInfoConstants.Retail.Name }),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets candidate executable file names for Generals and Zero Hour installations across all platforms and editions.
    /// </summary>
    public static IReadOnlyList<string> ValidGameExecutableNames => GameClientConstants.ValidGameExecutableNames;

    /// <summary>
    /// Gets candidate executable file names for Generals installations across all platforms and editions.
    /// </summary>
    public static IReadOnlyList<string> ValidGeneralsExecutableNames => GameClientConstants.ValidGeneralsExecutableNames;

    /// <summary>
    /// Gets candidate executable file names for Zero Hour installations across all platforms and editions.
    /// </summary>
    public static IReadOnlyList<string> ValidZeroHourExecutableNames => GameClientConstants.ValidZeroHourExecutableNames;

    /// <summary>
    /// Checks whether the directory contains a valid game executable in a case-insensitive manner.
    /// </summary>
    /// <param name="directoryPath">The directory path to check.</param>
    /// <returns>True if at least one recognized executable is found; otherwise false.</returns>
    public static bool HasValidGameExecutable(string? directoryPath)
    {
        return HasValidExecutableInternal(directoryPath, ValidGameExecutableNames);
    }

    /// <summary>
    /// Checks whether the directory contains a valid Generals executable in a case-insensitive manner.
    /// </summary>
    /// <param name="directoryPath">The directory path to check.</param>
    /// <returns>True if at least one recognized Generals executable is found; otherwise false.</returns>
    public static bool HasValidGeneralsExecutable(string? directoryPath)
    {
        return HasValidExecutableInternal(directoryPath, ValidGeneralsExecutableNames);
    }

    /// <summary>
    /// Checks whether the directory contains a valid Zero Hour executable in a case-insensitive manner.
    /// </summary>
    /// <param name="directoryPath">The directory path to check.</param>
    /// <returns>True if at least one recognized Zero Hour executable is found; otherwise false.</returns>
    public static bool HasValidZeroHourExecutable(string? directoryPath)
    {
        return HasValidExecutableInternal(directoryPath, ValidZeroHourExecutableNames);
    }

    /// <summary>
    /// Attempts to find a file in a case-insensitive manner, returning a path to the file if found.
    /// </summary>
    /// <param name="filePath">The full file path to check.</param>
    /// <param name="matchedPath">The actual on-disk path if found; otherwise null.</param>
    /// <returns>True if the file was found; otherwise false.</returns>
    public static bool TryGetFileCaseInsensitive(this string filePath, [NotNullWhen(true)] out string? matchedPath)
    {
        matchedPath = null;
        if (string.IsNullOrEmpty(filePath))
        {
            return false;
        }

        // First try direct filesystem check (efficient on Windows NTFS)
        if (File.Exists(filePath))
        {
            matchedPath = filePath;
            return true;
        }

        // Fallback: explicit case-insensitive search for case-sensitive filesystems
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            var fileName = Path.GetFileName(filePath);

            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
            {
                return false;
            }

            var directoryInfo = new DirectoryInfo(directory);
            if (!directoryInfo.Exists)
            {
                return false;
            }

            var matchingFile = directoryInfo.GetFiles().FirstOrDefault(f => string.Equals(f.Name, fileName, StringComparison.OrdinalIgnoreCase));
            if (matchingFile is not null)
            {
                matchedPath = matchingFile.FullName;
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Checks if a file exists in a case-insensitive manner, compatible across platforms.
    /// On Windows (NTFS), this leverages filesystem case-insensitivity.
    /// On Linux/macOS (case-sensitive filesystems), performs explicit case-insensitive search.
    /// </summary>
    /// <param name="filePath">The full file path to check.</param>
    /// <returns>True if the file exists (case-insensitive match).</returns>
    public static bool FileExistsCaseInsensitive(this string filePath)
    {
        return TryGetFileCaseInsensitive(filePath, out _);
    }

    /// <summary>
    /// Checks if a subdirectory exists under a parent path in a case-insensitive manner, returning the matched directory path.
    /// </summary>
    /// <param name="parentDirectory">The parent directory to search within.</param>
    /// <param name="subDirectoryName">The subdirectory name to look for.</param>
    /// <param name="matchedPath">The actual matched full path if found.</param>
    /// <returns>True if the subdirectory exists; otherwise false.</returns>
    public static bool TryGetDirectoryCaseInsensitive(this string parentDirectory, string subDirectoryName, [NotNullWhen(true)] out string? matchedPath)
    {
        matchedPath = null;
        if (string.IsNullOrEmpty(parentDirectory) || string.IsNullOrEmpty(subDirectoryName))
        {
            return false;
        }

        try
        {
            var candidate = Path.Combine(parentDirectory, subDirectoryName);
            if (Directory.Exists(candidate))
            {
                matchedPath = candidate;
                return true;
            }

            var directoryInfo = new DirectoryInfo(parentDirectory);
            if (!directoryInfo.Exists)
            {
                return false;
            }

            var matchingDir = directoryInfo.GetDirectories().FirstOrDefault(d => string.Equals(d.Name, subDirectoryName, StringComparison.OrdinalIgnoreCase));
            if (matchingDir is not null)
            {
                matchedPath = matchingDir.FullName;
                return true;
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Converts a platform-specific installation to the domain model.
    /// </summary>
    /// <param name="installation">The platform installation.</param>
    /// <param name="logger">Optional logger instance.</param>
    /// <returns>Domain model game installation.</returns>
    public static GameInstallation ToDomain(this IGameInstallation installation, ILogger? logger = null)
    {
        logger?.LogTrace(
            "Converting {InstallationType} installation to domain model",
            installation.InstallationType);

        // Use the original InstallationPath from the platform detector
        // This preserves the library root path (e.g., Steam library folder)
        // Only fall back to game-specific paths if InstallationPath is not set
        var installationPath = installation.InstallationPath;
        if (string.IsNullOrEmpty(installationPath))
        {
            installationPath = installation.HasGenerals ? installation.GeneralsPath : installation.ZeroHourPath;
        }

        var gameInstallation = new GameInstallation(installationPath, installation.InstallationType, logger as ILogger<GameInstallation>)
        {
            Id = installation.Id,
            DisplayName = installation.DisplayName,
        };
        gameInstallation.SetPaths(installation.GeneralsPath, installation.ZeroHourPath);
        gameInstallation.PopulateGameClients(installation.AvailableGameClients);

        logger?.LogTrace(
            "Successfully converted installation to domain model: {InstallationPath}, HasGenerals={HasGenerals}, HasZeroHour={HasZeroHour}",
            installationPath,
            gameInstallation.HasGenerals,
            gameInstallation.HasZeroHour);
        return gameInstallation;
    }

    /// <summary>
    /// Gets a human-readable display name for the installation type.
    /// Returns user-friendly names matching InstallationTypeDisplayConverter for UI consistency.
    /// </summary>
    /// <param name="installationType">The installation type.</param>
    /// <returns>Display name.</returns>
    public static string GetDisplayName(this GameInstallationType installationType)
    {
        return installationType switch
        {
            GameInstallationType.Steam => PublisherInfoConstants.Steam.Name,
            GameInstallationType.EaApp => PublisherInfoConstants.EaApp.Name,
            GameInstallationType.TheFirstDecade => PublisherInfoConstants.TheFirstDecade.Name,
            GameInstallationType.CDISO => PublisherInfoConstants.CdIso.Name,
            GameInstallationType.Wine => PublisherInfoConstants.Wine.Name,
            GameInstallationType.Retail => PublisherInfoConstants.Retail.Name,
            GameInstallationType.Lutris => PublisherInfoConstants.Lutris.Name,
            GameInstallationType.Custom => PublisherInfoConstants.GenHubLocal.Name,
            GameInstallationType.Unknown => GameClientConstants.UnknownVersion,
            _ => installationType.ToString(),
        };
    }

    /// <summary>
    /// Determines whether the specified identifier matches any known installation type identifier.
    /// </summary>
    /// <param name="identifier">The identifier to check.</param>
    /// <returns>True if the identifier represents an installation source; otherwise, false.</returns>
    public static bool IsInstallationIdentifier(string? identifier)
    {
        return !string.IsNullOrEmpty(identifier) && InstallationIdentifierSet.Contains(identifier);
    }

    /// <summary>
    /// Gets a normalized string representation for the installation type, suitable for manifest IDs and identifiers.
    /// Returns lowercase identifiers for consistency with the manifest ID system.
    /// </summary>
    /// <param name="installationType">The installation type.</param>
    /// <returns>A stable normalized lowercase string representation.</returns>
    public static string ToIdentifierString(this GameInstallationType installationType)
    {
        return installationType switch
        {
            GameInstallationType.Steam => "steam",
            GameInstallationType.EaApp => "eaapp",
            GameInstallationType.TheFirstDecade => "thefirstdecade",
            GameInstallationType.CDISO => "cdiso",
            GameInstallationType.Wine => "wine",
            GameInstallationType.Retail => "retail",
            GameInstallationType.Lutris => "lutris",
            GameInstallationType.Custom => "genhublocal",
            GameInstallationType.Unknown => "unknown",
            _ => throw new ArgumentOutOfRangeException(nameof(installationType), installationType, "Unknown installation type"),
        };
    }

    /// <summary>
    /// Determines if the installation type supports automatic updates.
    /// </summary>
    /// <param name="installationType">The installation type.</param>
    /// <returns>True if automatic updates are supported.</returns>
    public static bool SupportsAutomaticUpdates(this GameInstallationType installationType)
    {
        return installationType switch
        {
            GameInstallationType.Steam => true,
            GameInstallationType.EaApp => true,
            GameInstallationType.Wine => false,
            GameInstallationType.CDISO => false,
            GameInstallationType.Retail => false,
            GameInstallationType.Custom => false,
            _ => false,
        };
    }

    /// <summary>
    /// Determines if the installation type requires Wine/Proton compatibility layer.
    /// </summary>
    /// <param name="installationType">The installation type.</param>
    /// <returns>True if Wine/Proton is required.</returns>
    public static bool RequiresWineCompatibility(this GameInstallationType installationType)
    {
        return installationType == GameInstallationType.Wine;
    }

    /// <summary>
    /// Maps GameInstallationType enum to installation source identifier string.
    /// This is the canonical mapping used across the codebase for installation-source semantics.
    /// </summary>
    /// <param name="installationType">The game installation type to convert.</param>
    /// <returns>The corresponding installation source identifier string.</returns>
    public static string ToInstallationSourceString(this GameInstallationType installationType)
    {
        return installationType switch
        {
            GameInstallationType.Steam => "steam",
            GameInstallationType.EaApp => "eaapp",
            GameInstallationType.TheFirstDecade => "thefirstdecade",
            GameInstallationType.Wine => "wine",
            GameInstallationType.CDISO => "cdiso",
            GameInstallationType.Retail => "retail",
            GameInstallationType.Custom => "genhublocal",
            GameInstallationType.Unknown => "unknown",
            _ => "unknown",
        };
    }

    /// <summary>
    /// Maps GameInstallationType enum to publisher type identifier string.
    /// This mapping is intentionally separate because publisher semantics may differ from installation-source semantics.
    /// </summary>
    /// <param name="installationType">The game installation type to convert.</param>
    /// <returns>The corresponding publisher type identifier string.</returns>
    public static string ToPublisherTypeString(this GameInstallationType installationType)
    {
        return installationType switch
        {
            GameInstallationType.Steam => "steam",
            GameInstallationType.EaApp => "eaapp",

            // TheFirstDecade is mapped to Retail for publisher purposes (legacy/branding)
            GameInstallationType.TheFirstDecade => "retail",

            // Wine/Proton installations are treated as retail-published content
            GameInstallationType.Wine => "retail",
            GameInstallationType.CDISO => "retail",
            GameInstallationType.Retail => "retail",
            GameInstallationType.Custom => PublisherTypeConstants.GenHubLocal,
            GameInstallationType.Unknown => "unknown",
            _ => "unknown",
        };
    }

    /// <summary>
    /// Probes for the bundled base Generals assets within a Zero Hour directory (e.g. 'ZH_Generals'),
    /// returning the path if present and containing at least one retail archive (*.big).
    /// </summary>
    /// <remarks>
    /// A directory that exists but cannot be read is returned rather than treated as absent,
    /// so launch validation reports it as unreadable and refuses to spawn instead of silently
    /// starting Zero Hour without its base archives.
    /// </remarks>
    /// <param name="zeroHourPath">The Zero Hour installation path.</param>
    /// <returns>The path to the bundled base Generals directory if present and populated, or present but unreadable; otherwise <c>null</c>.</returns>
    public static string? GetBundledGeneralsPath(string? zeroHourPath)
    {
        if (string.IsNullOrWhiteSpace(zeroHourPath))
        {
            return null;
        }

        // The probe is the enumeration itself: Directory.Exists returns false for an
        // unreadable directory as well as a missing one, which would report a permission
        // problem as absent content. Only DirectoryNotFoundException means absence.
        var bundled = Path.Combine(zeroHourPath, GameClientConstants.ZhGeneralsDirectory);
        try
        {
            return Directory.EnumerateFiles(bundled, RetailArchiveConstants.ArchiveSearchPattern, RetailArchiveConstants.ArchiveSearch).Any()
                ? bundled
                : null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return bundled;
        }
    }

    /// <summary>
    /// Resolves the effective path to base Generals retail archives, checking <paramref name="generalsPath"/> first
    /// and falling back to <paramref name="bundledGeneralsPath"/> if present.
    /// </summary>
    /// <param name="generalsPath">The declared Generals path.</param>
    /// <param name="bundledGeneralsPath">The bundled Generals path.</param>
    /// <returns>The effective path to base Generals retail archives, or <c>null</c>.</returns>
    public static string? GetEffectiveGeneralsArchivePath(string? generalsPath, string? bundledGeneralsPath) =>
        !string.IsNullOrWhiteSpace(generalsPath) ? generalsPath : bundledGeneralsPath;

    private static bool HasValidExecutableInternal(string? directoryPath, IReadOnlyList<string> validExecutables)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return false;
        }

        // Fast path: exact-case check with no directory enumeration.
        if (validExecutables.Any(exe => File.Exists(Path.Combine(directoryPath, exe))))
        {
            return true;
        }

        // Slow path for case-sensitive filesystems holding case-variant names:
        // enumerate once and intersect instead of re-enumerating per candidate.
        try
        {
            var fileNames = new HashSet<string>(
                new DirectoryInfo(directoryPath).GetFiles().Select(f => f.Name),
                StringComparer.OrdinalIgnoreCase);
            return validExecutables.Any(fileNames.Contains);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
