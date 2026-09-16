using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.Checksum;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Services.Tools.Checksum;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Helpers;

/// <summary>
/// Centralized helper for Replay Manager CRC calculations, matching, and profile deduping logic.
/// </summary>
public static class ReplayCrcMatchingHelper
{
    private static readonly ConcurrentDictionary<string, (DateTime LastWriteTimeUtc, string Crc)> ExeCrcCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Normalizes a hexadecimal CRC string by trimming whitespace and optional '0x' prefix, converting to uppercase.
    /// </summary>
    /// <param name="value">The raw CRC string.</param>
    /// <returns>Normalized uppercase hex string without prefix.</returns>
    public static string NormalizeCrcHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[2..];
        }

        return trimmed.ToUpperInvariant();
    }

    /// <summary>
    /// Checks if a given CRC string corresponds to known retail Zero Hour 1.04 executables.
    /// </summary>
    /// <param name="crc">CRC string to test.</param>
    /// <returns><c>true</c> if it matches retail Zero Hour executable CRC; otherwise, <c>false</c>.</returns>
    public static bool IsZeroHourRetailExeCrc(string? crc) =>
        string.Equals(crc, ReplayManagerConstants.RetailZeroHourExeCrcFirstDecade, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(crc, ReplayManagerConstants.RetailZeroHourExeCrcSteam, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Checks if a given CRC string corresponds to known retail Generals 1.08 executables.
    /// </summary>
    /// <param name="crc">CRC string to test.</param>
    /// <returns><c>true</c> if it matches retail Generals executable CRC; otherwise, <c>false</c>.</returns>
    public static bool IsGeneralsRetailExeCrc(string? crc) =>
        string.Equals(crc, ReplayManagerConstants.RetailGeneralsExeCrcFirstDecade, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(crc, ReplayManagerConstants.RetailGeneralsExeCrcSteam, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Checks if a given CRC string corresponds to known retail executables for the specified game type.
    /// </summary>
    /// <param name="crc">CRC string to test.</param>
    /// <param name="gameType">The target game type.</param>
    /// <returns><c>true</c> if it matches retail executable CRC for the game type; otherwise, <c>false</c>.</returns>
    public static bool IsRetailExeCrc(string? crc, GameType gameType)
    {
        if (string.IsNullOrEmpty(crc))
        {
            return false;
        }

        return gameType switch
        {
            GameType.ZeroHour => IsZeroHourRetailExeCrc(crc),
            GameType.Generals => IsGeneralsRetailExeCrc(crc),
            _ => false,
        };
    }

    /// <summary>
    /// Determines whether two executable CRCs are equivalent across any supported retail game build or exact match.
    /// </summary>
    /// <param name="actualCrc">The calculated or actual executable CRC.</param>
    /// <param name="targetCrc">The target replay or manifest executable CRC.</param>
    /// <returns><c>true</c> if CRCs are equivalent; otherwise, <c>false</c>.</returns>
    public static bool AreExeCrcsEquivalent(string? actualCrc, string? targetCrc)
    {
        if (string.Equals(actualCrc, targetCrc, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IsZeroHourRetailExeCrc(actualCrc) && IsZeroHourRetailExeCrc(targetCrc))
        {
            return true;
        }

        return IsGeneralsRetailExeCrc(actualCrc) && IsGeneralsRetailExeCrc(targetCrc);
    }

    /// <summary>
    /// Determines whether two executable CRCs are equivalent, either exactly or via retail build equivalence for the specified game type.
    /// </summary>
    /// <param name="actualCrc">The calculated or actual executable CRC.</param>
    /// <param name="targetCrc">The target replay or manifest executable CRC.</param>
    /// <param name="gameType">The game type.</param>
    /// <returns><c>true</c> if CRCs are equivalent; otherwise, <c>false</c>.</returns>
    public static bool AreExeCrcsEquivalent(string? actualCrc, string? targetCrc, GameType gameType)
    {
        if (string.Equals(actualCrc, targetCrc, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsRetailExeCrc(actualCrc, gameType) && IsRetailExeCrc(targetCrc, gameType);
    }

    /// <summary>
    /// Determines whether the specified game profile is dedicated to another replay based on its description or name.
    /// </summary>
    /// <param name="profile">The game profile to examine.</param>
    /// <returns><c>true</c> if dedicated to another replay; otherwise, <c>false</c>.</returns>
    public static bool IsDedicatedToAnotherReplay(GameProfile profile)
    {
        if (profile == null)
        {
            return false;
        }

        var inDescription = !string.IsNullOrEmpty(profile.Description) &&
                            profile.Description.Contains("[replay:", StringComparison.OrdinalIgnoreCase);
        var inName = !string.IsNullOrEmpty(profile.Name) &&
                     profile.Name.Contains("(Replay:", StringComparison.OrdinalIgnoreCase);

        return inDescription || inName;
    }

    /// <summary>
    /// Returns the default executable name for a given game version and publisher.
    /// </summary>
    /// <param name="gameVersion">The game version.</param>
    /// <param name="publisher">The publisher name.</param>
    /// <returns>The executable filename.</returns>
    public static string GetDefaultExecutableName(GameType gameVersion, string? publisher)
    {
        if (gameVersion == GameType.Generals)
        {
            return GameClientConstants.GeneralsExecutable;
        }

        if (string.Equals(publisher, PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(publisher, PublisherTypeConstants.LegacySuperHackers, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(publisher, PublisherTypeConstants.CommunityOutpost, StringComparison.OrdinalIgnoreCase))
        {
            return GameClientConstants.SuperHackersZeroHourExecutable;
        }

        return GameClientConstants.ZeroHourExecutable;
    }

    /// <summary>
    /// Resolves the full executable path for a given game client.
    /// </summary>
    /// <param name="client">The game client.</param>
    /// <returns>The resolved full path, or <c>null</c> if not found or invalid.</returns>
    public static string? ResolveProfileFullExePath(GameClient? client)
    {
        if (client == null)
        {
            return null;
        }

        var exePath = client.ExecutablePath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            if (!string.IsNullOrWhiteSpace(client.WorkingDirectory))
            {
                var defaultExe = GetDefaultExecutableName(client.GameType, client.PublisherType);
                var candidate = Path.Combine(client.WorkingDirectory, defaultExe);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        if (!Path.IsPathRooted(exePath) && !string.IsNullOrWhiteSpace(client.WorkingDirectory))
        {
            exePath = Path.Combine(client.WorkingDirectory, exePath);
        }

        return exePath;
    }

    /// <summary>
    /// Retrieves a cached executable CRC if available and fresh.
    /// </summary>
    /// <param name="exePath">Path to the game client executable.</param>
    /// <returns>The cached CRC string, or <c>null</c> if missing or stale.</returns>
    public static string? GetCachedExeCrc(string exePath)
    {
        var fileInfo = new FileInfo(exePath);
        if (!fileInfo.Exists)
        {
            return null;
        }

        var lastWrite = fileInfo.LastWriteTimeUtc;
        if (ExeCrcCache.TryGetValue(exePath, out var cached) && cached.LastWriteTimeUtc == lastWrite)
        {
            return cached.Crc;
        }

        return null;
    }

    /// <summary>
    /// Computes or retrieves from cache the executable CRC for a given game client executable.
    /// </summary>
    /// <param name="exePath">Path to the game client executable.</param>
    /// <param name="crcCalculator">The game CRC calculator service.</param>
    /// <param name="logger">Optional logger instance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The calculated CRC string formatted as 0xXXXXXXXX, or null if calculation failed.</returns>
    public static async Task<string?> GetOrCalculateProfileExeCrcAsync(
        string exePath,
        IGameCrcCalculatorService crcCalculator,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(crcCalculator);
        try
        {
            var fileInfo = new FileInfo(exePath);
            if (!fileInfo.Exists)
            {
                return null;
            }

            var lastWrite = fileInfo.LastWriteTimeUtc;
            if (ExeCrcCache.TryGetValue(exePath, out var cached) && cached.LastWriteTimeUtc == lastWrite)
            {
                return cached.Crc;
            }

            var calcResult = await crcCalculator.CalculateExeCrcAsync(exePath, ct: ct);
            if (calcResult.Success && !string.IsNullOrEmpty(calcResult.Data))
            {
                ExeCrcCache[exePath] = (lastWrite, calcResult.Data);
                return calcResult.Data;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "[ReplayManager] Error calculating executable CRC for {ExePath}", exePath);
        }

        return null;
    }

    /// <summary>
    /// Computes or retrieves from cache the INI CRC for a given game installation root,
    /// delegating caching and file freshness checks to the CRC calculator.
    /// </summary>
    /// <param name="gameRoot">Path to the game installation root.</param>
    /// <param name="gameType">The game type.</param>
    /// <param name="crcCalculator">The game CRC calculator service.</param>
    /// <param name="logger">Optional logger instance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The calculated INI CRC string formatted as 0xXXXXXXXX, or null if calculation failed.</returns>
    public static async Task<string?> GetOrCalculateProfileIniCrcAsync(
        string gameRoot,
        GameType gameType,
        IGameCrcCalculatorService crcCalculator,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(crcCalculator);
        try
        {
            if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
            {
                return null;
            }

            var iniResult = await crcCalculator.CalculateIniCrcAsync(gameRoot, gameType, ct: ct);
            if (iniResult.Success && !string.IsNullOrEmpty(iniResult.Data))
            {
                return iniResult.Data;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "[ReplayManager] Error calculating INI CRC for {GameRoot}", gameRoot);
        }

        return null;
    }

    /// <summary>
    /// Preloads executable and INI CRCs for the given game profiles into cache.
    /// </summary>
    /// <param name="profiles">The collection of profiles to preload.</param>
    /// <param name="crcCalculator">The game CRC calculator service.</param>
    /// <param name="logger">Optional logger instance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the preload operation.</returns>
    public static async Task PreloadProfileCrcsAsync(
        IEnumerable<GameProfile> profiles,
        IGameCrcCalculatorService? crcCalculator,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        if (crcCalculator == null || profiles == null)
        {
            return;
        }

        foreach (var gameClient in profiles.Select(profile => profile.GameClient))
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            var exePath = ResolveProfileFullExePath(gameClient);
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                await GetOrCalculateProfileExeCrcAsync(exePath, crcCalculator, logger, ct);

                var gameRoot = Path.GetDirectoryName(exePath);
                if (!string.IsNullOrEmpty(gameRoot) && Directory.Exists(gameRoot) && gameClient != null)
                {
                    await GetOrCalculateProfileIniCrcAsync(gameRoot, gameClient.GameType, crcCalculator, logger, ct);
                }
            }
        }
    }

    /// <summary>
    /// Clears both executable and INI CRC caches.
    /// </summary>
    public static void ClearCrcCaches()
    {
        ExeCrcCache.Clear();
        GameCrcCalculatorService.ClearCache();
    }
}
