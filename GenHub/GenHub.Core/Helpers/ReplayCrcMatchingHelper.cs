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
    public static bool IsZeroHourRetailExeCrc(string? crc)
    {
        if (string.IsNullOrWhiteSpace(crc))
        {
            return false;
        }

        var normalized = NormalizeCrcHex(crc);
        return string.Equals(normalized, NormalizeCrcHex(ReplayManagerConstants.RetailZeroHourExeCrcFirstDecade), StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, NormalizeCrcHex(ReplayManagerConstants.RetailZeroHourExeCrcSteam), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if a given CRC string corresponds to known retail Generals 1.08 executables.
    /// </summary>
    /// <param name="crc">CRC string to test.</param>
    /// <returns><c>true</c> if it matches retail Generals executable CRC; otherwise, <c>false</c>.</returns>
    public static bool IsGeneralsRetailExeCrc(string? crc)
    {
        if (string.IsNullOrWhiteSpace(crc))
        {
            return false;
        }

        var normalized = NormalizeCrcHex(crc);
        return string.Equals(normalized, NormalizeCrcHex(ReplayManagerConstants.RetailGeneralsExeCrcFirstDecade), StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, NormalizeCrcHex(ReplayManagerConstants.RetailGeneralsExeCrcSteam), StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, NormalizeCrcHex("0x8F98E20A"), StringComparison.OrdinalIgnoreCase);
    }

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
    /// Determines whether the specified game client represents an official retail base game client
    /// (e.g. Generals 1.08 / 1.09, Zero Hour 1.04 / 1.05 from EA, EA App, Steam, or Retail distribution).
    /// </summary>
    /// <param name="client">The game client to evaluate.</param>
    /// <returns><c>true</c> if an official base game client; otherwise, <c>false</c>.</returns>
    public static bool IsOfficialBaseClient(GameClient? client)
    {
        if (client == null)
        {
            return false;
        }

        if (IsGeneralsOnlineClient(client) || IsLegacySuperHackersClient(client))
        {
            return false;
        }

        var pub = client.PublisherType;
        if (string.IsNullOrEmpty(pub) && !string.IsNullOrEmpty(client.Id))
        {
            var segments = client.Id.Split([ManifestConstants.ManifestIdSegmentSeparator], StringSplitOptions.None);
            if (segments.Length >= 4)
            {
                pub = segments[2];
            }
        }

        var normalizedPub = pub?.Trim().ToLowerInvariant() ?? string.Empty;
        var isOfficialPublisher = string.IsNullOrEmpty(normalizedPub) ||
                                  normalizedPub == PublisherTypeConstants.Steam ||
                                  normalizedPub == PublisherTypeConstants.Ea ||
                                  normalizedPub == PublisherTypeConstants.EaApp ||
                                  normalizedPub == PublisherTypeConstants.Retail ||
                                  normalizedPub == "ea" ||
                                  normalizedPub == "eaapp" ||
                                  normalizedPub == "steam" ||
                                  normalizedPub == "retail";

        if (!isOfficialPublisher && client.IsPublisherClient)
        {
            return false;
        }

        var ver = client.Version?.Trim() ?? string.Empty;
        var id = client.Id?.ToLowerInvariant() ?? string.Empty;

        if (client.GameType == GameType.Generals)
        {
            return ver.StartsWith("1.08", StringComparison.OrdinalIgnoreCase) ||
                   ver.StartsWith("1.09", StringComparison.OrdinalIgnoreCase) ||
                   ver == "1.8" ||
                   ver == "1.9" ||
                   id.Contains(".108.") ||
                   id.Contains(".109.") ||
                   !client.IsPublisherClient;
        }

        if (client.GameType == GameType.ZeroHour)
        {
            return ver.StartsWith("1.04", StringComparison.OrdinalIgnoreCase) ||
                   ver.StartsWith("1.05", StringComparison.OrdinalIgnoreCase) ||
                   ver == "1.4" ||
                   ver == "1.5" ||
                   id.Contains(".104.") ||
                   id.Contains(".105.") ||
                   !client.IsPublisherClient;
        }

        return !client.IsPublisherClient;
    }

    /// <summary>
    /// Determines whether the specified game client and enabled content are compatible with the retail Zero Hour executable CRC.
    /// </summary>
    /// <param name="client">The game client to evaluate.</param>
    /// <param name="enabledContentIds">Optional list of enabled content manifest IDs for the profile.</param>
    /// <returns>
    /// <c>true</c> if the client is compatible with retail Zero Hour executable CRC (e.g. 1.04 / 1.05);
    /// <c>false</c> if it uses a non-retail or custom executable (such as Generals Online or non-retail Community Patch).
    /// </returns>
    public static bool IsZeroHourRetailCompatible(GameClient? client, IReadOnlyList<string>? enabledContentIds = null)
    {
        if (client == null)
        {
            return false;
        }

        if (IsGeneralsOnlineClient(client) ||
            HasNonRetailIdentifier(client, enabledContentIds) ||
            IsLegacySuperHackersClient(client))
        {
            return false;
        }

        if (TryGetCachedExeCrc(client, out var cachedCrc))
        {
            if (IsZeroHourRetailExeCrc(cachedCrc))
            {
                return true;
            }

            return IsOfficialBaseClient(client);
        }

        return true;
    }

    /// <summary>
    /// Determines whether the specified game client is compatible with the retail executable CRC for its game type.
    /// For Zero Hour, checks compatibility with retail executable CRC (1.04 / 1.05).
    /// </summary>
    /// <param name="client">The game client to evaluate.</param>
    /// <param name="enabledContentIds">Optional list of enabled content manifest IDs for the profile.</param>
    /// <returns>
    /// <c>true</c> if compatible with retail executable CRC (e.g. retail 1.08 / 1.09 for Generals, retail 1.04 / 1.05 for Zero Hour);
    /// <c>false</c> if it uses a non-retail or custom executable.
    /// </returns>
    public static bool IsRetailCompatible(GameClient? client, IReadOnlyList<string>? enabledContentIds = null)
    {
        if (client == null)
        {
            return false;
        }

        var isExplicitGenerals = client.GameType == GameType.Generals &&
                                 !(!string.IsNullOrEmpty(client.Id) && client.Id.Contains("zerohour", StringComparison.OrdinalIgnoreCase)) &&
                                 !(!string.IsNullOrEmpty(client.Name) && client.Name.Contains("Zero Hour", StringComparison.OrdinalIgnoreCase));

        if (isExplicitGenerals)
        {
            if (IsGeneralsOnlineClient(client) || HasNonRetailIdentifier(client, enabledContentIds))
            {
                return false;
            }

            if (TryGetCachedExeCrc(client, out var cachedCrc))
            {
                if (IsGeneralsRetailExeCrc(cachedCrc))
                {
                    return true;
                }

                return IsOfficialBaseClient(client);
            }

            return true;
        }

        return IsZeroHourRetailCompatible(client, enabledContentIds);
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

    /// <summary>
    /// Tries to resolve the client's executable path and read its cached CRC.
    /// </summary>
    /// <param name="client">The game client to evaluate.</param>
    /// <param name="cachedCrc">The cached CRC string, if available and fresh.</param>
    /// <returns><c>true</c> if a cached CRC was found; otherwise, <c>false</c>.</returns>
    private static bool TryGetCachedExeCrc(GameClient client, out string? cachedCrc)
    {
        cachedCrc = null;
        var fullExePath = ResolveProfileFullExePath(client);
        if (string.IsNullOrWhiteSpace(fullExePath) || !File.Exists(fullExePath))
        {
            return false;
        }

        cachedCrc = GetCachedExeCrc(fullExePath);
        return !string.IsNullOrWhiteSpace(cachedCrc);
    }

    /// <summary>
    /// Determines whether the specified game client is a Generals Online client.
    /// </summary>
    /// <param name="client">The game client to evaluate.</param>
    /// <returns><c>true</c> if it is a Generals Online client; otherwise, <c>false</c>.</returns>
    private static bool IsGeneralsOnlineClient(GameClient client)
    {
        return string.Equals(client.PublisherType, PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(client.Name) && client.Name.Contains(GeneralsOnlineConstants.ClientName, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(client.Id) && client.Id.Contains(PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Determines whether the client or any enabled content uses a non-retail identifier.
    /// </summary>
    /// <param name="client">The game client to evaluate.</param>
    /// <param name="enabledContentIds">Optional list of enabled content manifest IDs for the profile.</param>
    /// <returns><c>true</c> if a non-retail identifier was found; otherwise, <c>false</c>.</returns>
    private static bool HasNonRetailIdentifier(GameClient client, IReadOnlyList<string>? enabledContentIds)
    {
        return CommunityOutpostConstants.IsNonRetailIdentifier(client.Id) ||
            CommunityOutpostConstants.IsNonRetailIdentifier(client.Name) ||
            CommunityOutpostConstants.IsNonRetailIdentifier(client.PublisherType) ||
            (enabledContentIds != null && enabledContentIds.Any(CommunityOutpostConstants.IsNonRetailIdentifier));
    }

    /// <summary>
    /// Determines whether the specified game client is a legacy SuperHackers client rather than a Community Patch build.
    /// </summary>
    /// <param name="client">The game client to evaluate.</param>
    /// <returns><c>true</c> if it is a legacy SuperHackers client; otherwise, <c>false</c>.</returns>
    private static bool IsLegacySuperHackersClient(GameClient client)
    {
        return (string.Equals(client.PublisherType, PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(client.PublisherType, PublisherTypeConstants.LegacySuperHackers, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(client.Id) && client.Id.Contains(PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase))) &&
            !string.Equals(client.PublisherType, CommunityOutpostConstants.PublisherType, StringComparison.OrdinalIgnoreCase) &&
            (client.Name == null || !client.Name.Contains(CommunityOutpostConstants.ContentName, StringComparison.OrdinalIgnoreCase));
    }
}
