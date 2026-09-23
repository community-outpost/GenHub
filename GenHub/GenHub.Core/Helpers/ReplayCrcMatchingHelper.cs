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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Helpers;

/// <summary>
/// Centralized helper for Replay Manager CRC calculations, matching, and profile deduping logic.
/// </summary>
public static class ReplayCrcMatchingHelper
{
    /// <summary>
    /// Known SHA-256 hashes for official retail executables (Generals 1.08, 1.09 and Zero Hour 1.04, 1.05).
    /// </summary>
    public static readonly string[] RetailExeSha256Hashes =
    [
        "1c96366ff6a99f40863f6bbcfa8bf7622e8df1f80a474201e0e95e37c6416255", // Steam Generals 1.09
        "69A39881179112A566CEF69573B20065CC868516C49AF0761F809EC57DA0BDBC", // EA App Generals 1.08
        "8DDE6C990280AC44B4629A664B24BBAF226E629E9C7700234010F198783B6674", // EA App Generals generals.exe (wrapper)
        "7B075B9F0BAA9DF81651C0C9DD7D8C445454AE1B2452B928F4A1D9332E9CCECE", // Steam Zero Hour 1.04
        "253FEBA0A5503CB4D49FD07463B17D3CC84731E583F9625CB90FCD8B5CAC0221", // EA App Zero Hour 1.04
        "FF6F78211A014100D8EF6B08BC2F8EDD3D55E99E872DFDB5371776FC5A5D02CE", // EA App / Steam Zero Hour generals.exe (wrapper)
        "f37a4929f8d697104e99c2bcf46f8d833122c943afcd87fd077df641d344495b", // Retail Zero Hour 1.04
        "420fba1dbdc4c14e2418c2b0d3010b9fac6f314eafa1f3a101805b8d98883ea1", // Community Outpost Zero Hour 1.05
        "a531a56e82381b0d15117ee5f9881d276de232c9245e598f3b75b71bc80275b8", // TheSuperHackers / Community Patch weekly build (23-07-2026)
    ];

    private static readonly ConcurrentDictionary<string, (DateTime LastWriteTimeUtc, string Crc)> ExeCrcCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, (DateTime LastWriteTimeUtc, string Sha256)> ExeShaCache = new(StringComparer.OrdinalIgnoreCase);

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
               string.Equals(normalized, NormalizeCrcHex(ReplayManagerConstants.RetailZeroHourExeCrcSteam), StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, NormalizeCrcHex(ReplayManagerConstants.RetailZeroHourExeCrcCommunityPatch), StringComparison.OrdinalIgnoreCase);
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
               string.Equals(normalized, NormalizeCrcHex(ReplayManagerConstants.RetailGeneralsExeCrcEaApp), StringComparison.OrdinalIgnoreCase);
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
    /// Checks if a given INI CRC string corresponds to known retail Zero Hour 1.04 configuration.
    /// </summary>
    /// <param name="crc">INI CRC string to test.</param>
    /// <returns><c>true</c> if it matches retail Zero Hour INI CRC; otherwise, <c>false</c>.</returns>
    public static bool IsZeroHourRetailIniCrc(string? crc)
    {
        if (string.IsNullOrWhiteSpace(crc))
        {
            return false;
        }

        var normalized = NormalizeCrcHex(crc);
        return string.Equals(normalized, NormalizeCrcHex(ReplayManagerConstants.RetailZeroHourIniCrcVanilla), StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, NormalizeCrcHex(ReplayManagerConstants.RetailZeroHourIniCrcAlternate), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if a given INI CRC string corresponds to known retail Generals 1.08 / 1.09 configuration.
    /// </summary>
    /// <param name="crc">INI CRC string to test.</param>
    /// <returns><c>true</c> if it matches retail Generals INI CRC; otherwise, <c>false</c>.</returns>
    public static bool IsGeneralsRetailIniCrc(string? crc)
    {
        if (string.IsNullOrWhiteSpace(crc))
        {
            return false;
        }

        var normalized = NormalizeCrcHex(crc);
        return string.Equals(normalized, NormalizeCrcHex(ReplayManagerConstants.RetailGeneralsIniCrcVanilla), StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, NormalizeCrcHex(ReplayManagerConstants.RetailGeneralsIniCrcGerman), StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, NormalizeCrcHex(ReplayManagerConstants.RetailGeneralsIniCrcSteam), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if a given INI CRC string corresponds to known retail configuration for the specified game type.
    /// </summary>
    /// <param name="crc">INI CRC string to test.</param>
    /// <param name="gameType">The target game type.</param>
    /// <returns><c>true</c> if it matches retail INI CRC for the game type; otherwise, <c>false</c>.</returns>
    public static bool IsRetailIniCrc(string? crc, GameType gameType)
    {
        if (string.IsNullOrEmpty(crc))
        {
            return false;
        }

        return gameType switch
        {
            GameType.ZeroHour => IsZeroHourRetailIniCrc(crc),
            GameType.Generals => IsGeneralsRetailIniCrc(crc),
            _ => false,
        };
    }

    /// <summary>
    /// Checks if a given SHA-256 string corresponds to known retail executables (Generals 1.08, 1.09, Zero Hour 1.04, 1.05).
    /// </summary>
    /// <param name="sha">SHA-256 string to test.</param>
    /// <returns><c>true</c> if it matches a known retail executable SHA-256; otherwise, <c>false</c>.</returns>
    public static bool IsRetailExeSha256(string? sha)
    {
        if (string.IsNullOrWhiteSpace(sha))
        {
            return false;
        }

        var normalized = sha.Trim();
        return RetailExeSha256Hashes.Any(h => string.Equals(h, normalized, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Computes and caches the SHA-256 hash of an executable file on disk.
    /// </summary>
    /// <param name="exePath">The full path to the executable file.</param>
    /// <returns>The hex-encoded SHA-256 hash, or null if the file does not exist or cannot be read.</returns>
    public static string? GetCachedExeSha256(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            return null;
        }

        try
        {
            var lastWrite = File.GetLastWriteTimeUtc(exePath);
            if (ExeShaCache.TryGetValue(exePath, out var cached) && cached.LastWriteTimeUtc == lastWrite)
            {
                return cached.Sha256;
            }

            using var stream = File.OpenRead(exePath);
            using var sha = SHA256.Create();
            var hashBytes = sha.ComputeHash(stream);
            var hash = Convert.ToHexString(hashBytes);
            ExeShaCache[exePath] = (lastWrite, hash);
            return hash;
        }
        catch
        {
            return null;
        }
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
    /// Determines whether the specified game client is a retail-compatible SuperHackers client.
    /// </summary>
    /// <param name="client">The game client to evaluate.</param>
    /// <returns><c>true</c> if it is a retail-compatible SuperHackers client; otherwise, <c>false</c>.</returns>
    public static bool IsSuperHackersRetailClient(GameClient client)
    {
        var pub = client.PublisherType ?? string.Empty;
        var id = client.Id ?? string.Empty;
        var name = client.Name ?? string.Empty;

        var isTsh = string.Equals(pub, PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(pub, PublisherTypeConstants.LegacySuperHackers, StringComparison.OrdinalIgnoreCase) ||
                    id.Contains(PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) ||
                    name.Contains(PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("SuperHackers", StringComparison.OrdinalIgnoreCase);

        if (!isTsh)
        {
            return false;
        }

        return !CommunityOutpostConstants.IsNonRetailIdentifier(id) &&
               !CommunityOutpostConstants.IsNonRetailIdentifier(name);
    }

    /// <summary>
    /// Determines whether the specified game client represents an official retail base game client
    /// (e.g. Generals 1.08 / 1.09, Zero Hour 1.04 / 1.05 from EA, EA App, Steam, or Retail distribution).
    /// </summary>
    /// <param name="client">The game client to evaluate.</param>
    /// <returns><c>true</c> if an official base game client; otherwise, <c>false</c>.</returns>
    public static bool IsOfficialBaseClient(GameClient? client)
    {
        if (client == null || IsNonRetailEngineClient(client))
        {
            return false;
        }

        if (IsSuperHackersRetailClient(client) || IsCommunityOutpostRetailClient(client))
        {
            return true;
        }

        if (!IsOfficialPublisher(client) && client.IsPublisherClient)
        {
            return false;
        }

        return client.GameType switch
        {
            GameType.Generals => IsGeneralsRetailVersion(client),
            GameType.ZeroHour => IsZeroHourRetailVersion(client),
            _ => !client.IsPublisherClient,
        };
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

        return IsClientRetailCompatible(client, enabledContentIds, IsZeroHourRetailIniCrc, IsZeroHourRetailExeCrc);
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

        if (IsExplicitGeneralsClient(client))
        {
            return IsGeneralsRetailCompatible(client, enabledContentIds);
        }

        return IsZeroHourRetailCompatible(client, enabledContentIds);
    }

    /// <summary>
    /// Determines whether the specified game client is a Generals Online client.
    /// </summary>
    /// <param name="client">The game client to evaluate.</param>
    /// <returns><c>true</c> if it is a Generals Online client; otherwise, <c>false</c>.</returns>
    public static bool IsGeneralsOnlineClient(GameClient client)
    {
        return string.Equals(client.PublisherType, PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(client.Name) && client.Name.Contains(GeneralsOnlineConstants.ClientName, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(client.Id) && client.Id.Contains(PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Determines whether the specified game client is a GeneralsX engine port or fork.
    /// </summary>
    /// <param name="client">The game client to evaluate.</param>
    /// <returns><c>true</c> if the client is GeneralsX; otherwise, <c>false</c>.</returns>
    public static bool IsGeneralsXClient(GameClient client)
    {
        return (!string.IsNullOrEmpty(client.Id) && (client.Id.Contains(PublisherTypeConstants.GeneralsX, StringComparison.OrdinalIgnoreCase) || client.Id.Contains(PublisherTypeConstants.Fbraz3, StringComparison.OrdinalIgnoreCase))) ||
            (!string.IsNullOrEmpty(client.Name) && (client.Name.Contains(PublisherTypeConstants.GeneralsX, StringComparison.OrdinalIgnoreCase) || client.Name.Contains("generals x", StringComparison.OrdinalIgnoreCase))) ||
            (!string.IsNullOrEmpty(client.ExecutablePath) && client.ExecutablePath.Contains(PublisherTypeConstants.GeneralsX, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(client.PublisherType, PublisherTypeConstants.GeneralsX, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(client.PublisherType, PublisherTypeConstants.Fbraz3, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether the specified game client is a legacy SuperHackers client rather than a Community Patch build.
    /// </summary>
    /// <param name="client">The game client to evaluate.</param>
    /// <returns><c>true</c> if it is a legacy SuperHackers client; otherwise, <c>false</c>.</returns>
    public static bool IsLegacySuperHackersClient(GameClient client)
    {
        return (string.Equals(client.PublisherType, PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(client.PublisherType, PublisherTypeConstants.LegacySuperHackers, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(client.Id) && client.Id.Contains(PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(client.Id) && client.Id.Contains(PublisherTypeConstants.LegacySuperHackers, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(client.Name) && (client.Name.Contains(SuperHackersConstants.NameMarker, StringComparison.OrdinalIgnoreCase) || client.Name.Contains(PublisherTypeConstants.TheSuperHackersDisplayName, StringComparison.OrdinalIgnoreCase)))) &&
            !string.Equals(client.PublisherType, CommunityOutpostConstants.PublisherType, StringComparison.OrdinalIgnoreCase) &&
            (client.Name == null || !client.Name.Contains(CommunityOutpostConstants.ContentName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Determines whether the specified executable path uses a non-retail executable format
    /// (such as Linux Flatpak, AppImage, macOS bundle, or non-PE binaries), which cannot match retail Windows PE executable CRCs or run inside Wine/Proton.
    /// </summary>
    /// <param name="executablePath">The executable path to evaluate.</param>
    /// <returns><c>true</c> if the executable format is non-retail/non-PE; otherwise, <c>false</c>.</returns>
    public static bool HasNonRetailExecutableFormat(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var ext = Path.GetExtension(executablePath);
        if (string.IsNullOrEmpty(ext))
        {
            return true;
        }

        return !string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether the specified game client uses a non-retail executable format
    /// (such as Linux Flatpak, AppImage, or non-PE binaries), which cannot match retail Windows PE executable CRCs.
    /// </summary>
    /// <param name="client">The game client to evaluate.</param>
    /// <returns><c>true</c> if the client executable format is non-retail/non-PE; otherwise, <c>false</c>.</returns>
    public static bool HasNonRetailExecutableFormat(GameClient? client)
    {
        return HasNonRetailExecutableFormat(client?.ExecutablePath);
    }

    /// <summary>
    /// Determines whether a game client and installation type are eligible for Steam integration launch.
    /// Steam integration requires a Steam installation and a Windows retail PE executable format.
    /// </summary>
    /// <param name="installationType">The game installation type.</param>
    /// <param name="client">The game client to evaluate.</param>
    /// <returns><c>true</c> if eligible for Steam launch; otherwise, <c>false</c>.</returns>
    public static bool IsSteamLaunchEligible(GameInstallationType installationType, GameClient? client)
    {
        return installationType == GameInstallationType.Steam && !HasNonRetailExecutableFormat(client);
    }

    /// <summary>
    /// Determines whether an executable path and installation type are eligible for Steam integration launch.
    /// Steam integration requires a Steam installation and a Windows retail PE executable format.
    /// </summary>
    /// <param name="installationType">The game installation type.</param>
    /// <param name="executablePath">The executable path to evaluate.</param>
    /// <returns><c>true</c> if eligible for Steam launch; otherwise, <c>false</c>.</returns>
    public static bool IsSteamLaunchEligible(GameInstallationType installationType, string? executablePath)
    {
        return installationType == GameInstallationType.Steam && !HasNonRetailExecutableFormat(executablePath);
    }

    /// <summary>
    /// Determines whether a game client on a Steam installation is eligible for Steam integration launch.
    /// </summary>
    /// <param name="isSteamInstallation">Whether the installation is a Steam installation.</param>
    /// <param name="client">The game client to evaluate.</param>
    /// <returns><c>true</c> if eligible for Steam launch; otherwise, <c>false</c>.</returns>
    public static bool IsSteamLaunchEligible(bool isSteamInstallation, GameClient? client)
    {
        return isSteamInstallation && !HasNonRetailExecutableFormat(client);
    }

    /// <summary>
    /// Determines whether the specified game client represents a non-retail community engine fork
    /// (such as GeneralsX, TheSuperHackers, Generals Online, or a non-Windows binary).
    /// </summary>
    /// <param name="client">The game client to evaluate.</param>
    /// <returns><c>true</c> if the client is a non-retail engine fork or binary; otherwise, <c>false</c>.</returns>
    public static bool IsNonRetailEngineClient(GameClient client)
    {
        return IsGeneralsOnlineClient(client) ||
            IsLegacySuperHackersClient(client) ||
            IsGeneralsXClient(client) ||
            HasNonRetailExecutableFormat(client);
    }

    /// <summary>
    /// Asynchronously determines whether a game profile is retail compatible, calculating the INI CRC in the background if necessary.
    /// </summary>
    /// <param name="profile">The game profile to evaluate.</param>
    /// <param name="crcCalculator">Optional game CRC calculator service.</param>
    /// <param name="logger">Optional logger instance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if compatible with retail gameplay; otherwise, <c>false</c>.</returns>
    public static async Task<bool> IsRetailCompatibleAsync(
        GameProfile? profile,
        IGameCrcCalculatorService? crcCalculator = null,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        if (profile?.GameClient == null)
        {
            return false;
        }

        var client = profile.GameClient;
        if (IsNonRetailEngineClient(client) || HasNonRetailIdentifier(client, profile.EnabledContentIds))
        {
            return false;
        }

        if (IsRetailCompatible(client, profile.EnabledContentIds))
        {
            return true;
        }

        if (TryGetCachedIniCrc(client, out var cachedIniCrc) && IsRetailIniCrc(cachedIniCrc, client.GameType))
        {
            return true;
        }

        var exePath = ResolveProfileFullExePath(client);
        var gameRoot = !string.IsNullOrEmpty(exePath) ? Path.GetDirectoryName(exePath) : null;

        if (crcCalculator != null && !string.IsNullOrEmpty(gameRoot) && Directory.Exists(gameRoot))
        {
            var iniResult = await crcCalculator.CalculateIniCrcAsync(gameRoot, client.GameType, ct: ct);
            if (iniResult.Success && !string.IsNullOrEmpty(iniResult.Data) && IsRetailIniCrc(iniResult.Data, client.GameType))
            {
                return true;
            }
        }

        return false;
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
    /// Clears executable and INI CRC and SHA caches.
    /// </summary>
    public static void ClearCrcCaches()
    {
        ExeCrcCache.Clear();
        ExeShaCache.Clear();
        GameCrcCalculatorService.ClearCache();
    }

    /// <summary>
    /// Determines whether the specified client is explicitly for Generals rather than Zero Hour.
    /// </summary>
    /// <param name="client">The game client to evaluate.</param>
    /// <returns><c>true</c> if explicitly Generals; otherwise, <c>false</c>.</returns>
    private static bool IsExplicitGeneralsClient(GameClient client)
    {
        if (client.GameType != GameType.Generals)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(client.Id) && client.Id.Contains("zerohour", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(client.Name) && client.Name.Contains("Zero Hour", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Determines whether the Generals game client is compatible with retail executables.
    /// </summary>
    /// <param name="client">The game client to evaluate.</param>
    /// <param name="enabledContentIds">Optional list of enabled content manifest IDs.</param>
    /// <returns><c>true</c> if compatible with retail executables; otherwise, <c>false</c>.</returns>
    private static bool IsGeneralsRetailCompatible(GameClient client, IReadOnlyList<string>? enabledContentIds)
    {
        return IsClientRetailCompatible(client, enabledContentIds, IsGeneralsRetailIniCrc, IsGeneralsRetailExeCrc);
    }

    /// <summary>
    /// Determines whether the specified game client and enabled content are compatible with retail executables
    /// using game-specific CRC matching predicates.
    /// </summary>
    private static bool IsClientRetailCompatible(
        GameClient client,
        IReadOnlyList<string>? enabledContentIds,
        Func<string?, bool> isRetailIniCrc,
        Func<string?, bool> isRetailExeCrc)
    {
        if (IsNonRetailEngineClient(client) || HasNonRetailIdentifier(client, enabledContentIds))
        {
            return false;
        }

        if (IsSuperHackersRetailClient(client) || IsCommunityOutpostRetailClient(client))
        {
            return true;
        }

        if (TryGetCachedIniCrc(client, out var cachedIniCrc) && isRetailIniCrc(cachedIniCrc))
        {
            return true;
        }

        if (TryGetCachedExeCrc(client, out var cachedCrc) && isRetailExeCrc(cachedCrc))
        {
            return true;
        }

        var exePath = ResolveProfileFullExePath(client);
        if (MatchesExeOrGameDat(exePath, isRetailExeCrc))
        {
            return true;
        }

        return IsOfficialBaseClient(client);
    }

    /// <summary>
    /// Checks whether the resolved executable or its adjacent game.dat matches known retail signatures.
    /// </summary>
    private static bool MatchesExeOrGameDat(string? exePath, Func<string?, bool> isRetailExeCrc)
    {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            return false;
        }

        var sha = GetCachedExeSha256(exePath);
        if (!string.IsNullOrEmpty(sha) && IsRetailExeSha256(sha))
        {
            return true;
        }

        return MatchesGameDat(exePath, isRetailExeCrc);
    }

    /// <summary>
    /// Checks whether game.dat adjacent to the executable matches known retail signatures.
    /// </summary>
    private static bool MatchesGameDat(string exePath, Func<string?, bool> isRetailExeCrc)
    {
        var dir = Path.GetDirectoryName(exePath);
        if (string.IsNullOrEmpty(dir))
        {
            return false;
        }

        var gameDatPath = Path.Combine(dir, GameClientConstants.SteamGameDatExecutable);
        if (!File.Exists(gameDatPath))
        {
            return false;
        }

        var datCrc = GetCachedExeCrc(gameDatPath);
        if (!string.IsNullOrEmpty(datCrc) && isRetailExeCrc(datCrc))
        {
            return true;
        }

        var datSha = GetCachedExeSha256(gameDatPath);
        return !string.IsNullOrEmpty(datSha) && IsRetailExeSha256(datSha);
    }

    /// <summary>
    /// Tries to resolve the client's game root path and read its cached INI CRC.
    /// </summary>
    /// <param name="client">The game client to evaluate.</param>
    /// <param name="cachedIniCrc">The cached INI CRC string, if available and fresh.</param>
    /// <returns><c>true</c> if a cached INI CRC was found; otherwise, <c>false</c>.</returns>
    private static bool TryGetCachedIniCrc(GameClient client, out string? cachedIniCrc)
    {
        cachedIniCrc = null;
        var fullExePath = ResolveProfileFullExePath(client);
        if (string.IsNullOrWhiteSpace(fullExePath))
        {
            return false;
        }

        var root = Path.GetDirectoryName(fullExePath);
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            return false;
        }

        cachedIniCrc = GameCrcCalculatorService.GetCachedIniCrcStatic(root, client.GameType);
        return !string.IsNullOrWhiteSpace(cachedIniCrc);
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
        if (string.IsNullOrWhiteSpace(fullExePath))
        {
            return false;
        }

        if (!File.Exists(fullExePath))
        {
            return false;
        }

        cachedCrc = GetCachedExeCrc(fullExePath);
        return cachedCrc != null;
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

    private static bool IsOfficialPublisher(GameClient client)
    {
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
        return string.IsNullOrEmpty(normalizedPub) ||
               normalizedPub == PublisherTypeConstants.Steam ||
               normalizedPub == PublisherTypeConstants.Ea ||
               normalizedPub == PublisherTypeConstants.EaApp ||
               normalizedPub == PublisherTypeConstants.Retail ||
               normalizedPub == "electronic arts" ||
               normalizedPub == "ea" ||
               normalizedPub == "ea app" ||
               normalizedPub == "eaapp" ||
               normalizedPub == "community outpost" ||
               normalizedPub == PublisherTypeConstants.CommunityOutpost;
    }

    private static bool IsCommunityOutpostRetailClient(GameClient client)
    {
        var pub = client.PublisherType ?? string.Empty;
        var id = client.Id ?? string.Empty;
        var name = client.Name ?? string.Empty;

        var isCoPublisher = string.Equals(pub, PublisherTypeConstants.CommunityOutpost, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(pub, "community outpost", StringComparison.OrdinalIgnoreCase) ||
                            id.Contains("communityoutpost", StringComparison.OrdinalIgnoreCase) ||
                            id.Contains("community-outpost", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Community Patch", StringComparison.OrdinalIgnoreCase);

        if (!isCoPublisher)
        {
            return false;
        }

        return !CommunityOutpostConstants.IsNonRetailIdentifier(id) &&
               !CommunityOutpostConstants.IsNonRetailIdentifier(name);
    }

    private static bool IsGeneralsRetailVersion(GameClient client)
    {
        if (!client.IsPublisherClient)
        {
            return true;
        }

        var ver = client.Version?.Trim().TrimStart('v', 'V').Trim() ?? string.Empty;
        var id = client.Id ?? string.Empty;
        var name = client.Name ?? string.Empty;

        return ver.StartsWith("1.08", StringComparison.OrdinalIgnoreCase) ||
               ver.StartsWith("1.09", StringComparison.OrdinalIgnoreCase) ||
               ver == "1.8" ||
               ver == "1.9" ||
               id.Contains(".108.", StringComparison.OrdinalIgnoreCase) ||
               id.Contains(".109.", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("1.08", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("1.09", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsZeroHourRetailVersion(GameClient client)
    {
        if (!client.IsPublisherClient)
        {
            return true;
        }

        var ver = client.Version?.Trim().TrimStart('v', 'V').Trim() ?? string.Empty;
        var id = client.Id ?? string.Empty;
        var name = client.Name ?? string.Empty;

        return ver.StartsWith("1.04", StringComparison.OrdinalIgnoreCase) ||
               ver.StartsWith("1.05", StringComparison.OrdinalIgnoreCase) ||
               ver == "1.4" ||
               ver == "1.5" ||
               id.Contains(".104.", StringComparison.OrdinalIgnoreCase) ||
               id.Contains(".105.", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("1.04", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("1.05", StringComparison.OrdinalIgnoreCase);
    }
}
