using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Online;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace GenHub.Core.Helpers;

/// <summary>
/// Matches local game profiles against a lobby's expected profile.
/// The fingerprint binds the game client key to a hash of the sorted
/// gameplay-affecting content ids (mods, patches); cosmetics such as UI
/// addons, skins, maps, and media never affect the match, mirroring the exe
/// and ini CRC inputs that decide whether two setups can share a game.
/// Hashing keeps every fingerprint well under the edge 256-character cap no
/// matter how many content ids a profile carries.
/// </summary>
public static class OnlineProfileMatcher
{
    private const int FingerprintHashChars = 16;

    /// <summary>
    /// Segment count of a versioned compatibility fingerprint:
    /// prefix, game type, version, client id, content hash, iniCRC, exeCRC.
    /// </summary>
    private const int CompatibilitySegmentCount = 7;

    /// <summary>
    /// Determines whether a content type can affect game sync and therefore
    /// belongs in the profile fingerprint.
    /// </summary>
    /// <param name="type">The content type.</param>
    /// <returns>True for gameplay-affecting content.</returns>
    public static bool IsGameplayContent(ContentType type) => type switch
    {
        ContentType.GameClient => false,
        ContentType.GameInstallation => false,
        ContentType.Map => false,
        ContentType.MapPack => false,
        ContentType.Mission => false,
        ContentType.Mod => true,
        ContentType.Patch => true,
        ContentType.ContentBundle => true,
        ContentType.Executable => true,
        _ => false,
    };

    /// <summary>
    /// Builds the cross-machine game client key for a profile.
    /// </summary>
    /// <param name="profile">The game profile.</param>
    /// <returns>The client key, or empty when the profile has no client.</returns>
    public static string GetGameClientKey(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var client = profile.GameClient;
        if (client is null)
        {
            return string.Empty;
        }

        return string.Join(
            OnlineConstants.FingerprintSeparator,
            client.GameType.ToString(),
            client.Version ?? string.Empty,
            client.Id ?? string.Empty);
    }

    /// <summary>
    /// Resolves the effective content type when one manifest id is registered
    /// under several types. Duplicate registrations enumerate in an order the
    /// caller does not control, so the pick must be deterministic or two
    /// machines with the same setup classify the id differently and report a
    /// phantom mod mismatch. Gameplay wins ties: a disputed id surfaces as a
    /// mismatch, never as a silent match.
    /// </summary>
    /// <param name="candidates">The registered types for one manifest id.</param>
    /// <returns>The deterministic effective type.</returns>
    public static ContentType ResolveDeclaredType(IEnumerable<ContentType> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .OrderBy(t => IsGameplayContent(t) ? 0 : 1)
            .ThenBy(t => t.ToString(), StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>
    /// Determines whether a content ID affects gameplay sync.
    /// Only genuine gameplay-affecting content (mods, patches, bundles, executables)
    /// belongs in the fingerprint; maps, map packs, and missions are local assets
    /// that do not alter the base game rules or engine INI CRC.
    /// </summary>
    /// <param name="contentId">The content identifier to test.</param>
    /// <param name="contentTypes">Optional content type map.</param>
    /// <returns>True if gameplay affecting; otherwise false.</returns>
    public static bool IsContentGameplayAffecting(
        string contentId,
        IReadOnlyDictionary<string, ContentType>? contentTypes)
    {
        if (string.IsNullOrWhiteSpace(contentId))
        {
            return false;
        }

        if (contentTypes != null && contentTypes.TryGetValue(contentId, out var type))
        {
            return IsGameplayContent(type);
        }

        var lower = contentId.ToLowerInvariant();
        var segments = lower.Split('.');
        if (segments.Contains("map") ||
            segments.Contains("mappack") ||
            segments.Contains("mission") ||
            segments.Contains("customasset"))
        {
            return false;
        }

        if (segments.Contains("mod") ||
            segments.Contains("patch") ||
            segments.Contains("contentbundle") ||
            segments.Contains("executable"))
        {
            return true;
        }

        // For backward compatibility with tests or unrecognized content types:
        // unrecognized non-map content defaults to true to avoid silent desync.
        return true;
    }

    /// <summary>
    /// Lists the profile's gameplay content ids in stable order. Content with
    /// an unknown type counts as gameplay: an unrecognized id must surface as
    /// a mismatch, never as a silent match. Maps and map packs are excluded.
    /// </summary>
    /// <param name="profile">The game profile.</param>
    /// <param name="contentTypes">Manifest id to content type lookup, or null when unavailable.</param>
    /// <returns>The sorted gameplay content ids.</returns>
    public static IReadOnlyList<string> GetGameplayContentIds(
        GameProfile profile,
        IReadOnlyDictionary<string, ContentType>? contentTypes)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return profile.EnabledContentIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Where(id => IsContentGameplayAffecting(id, contentTypes))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Computes the cross-machine fingerprint for a profile.
    /// </summary>
    /// <param name="profile">The game profile.</param>
    /// <param name="contentTypes">Manifest id to content type lookup, or null when unavailable.</param>
    /// <returns>The fingerprint string.</returns>
    public static string ComputeFingerprint(
        GameProfile profile,
        IReadOnlyDictionary<string, ContentType>? contentTypes)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var clientKey = GetGameClientKey(profile);
        var gameplay = GetGameplayContentIds(profile, contentTypes);
        return CreateFingerprint(clientKey, gameplay);
    }

    /// <summary>
    /// Builds a fingerprint from resolved setup parts, embedding the engine
    /// compatibility CRCs when available. Without CRCs the output is
    /// byte-identical to the previous version, so id-only fingerprints keep
    /// matching exactly as before.
    /// </summary>
    /// <param name="clientKey">The game client key.</param>
    /// <param name="gameplayContentIds">The sorted gameplay content ids.</param>
    /// <param name="iniCrc">The engine iniCRC (0xXXXXXXXX) or empty when unavailable.</param>
    /// <param name="exeCrc">The engine exeCRC (0xXXXXXXXX) or empty when unavailable.</param>
    /// <returns>The fingerprint string.</returns>
    public static string CreateFingerprint(
        string clientKey,
        IReadOnlyList<string> gameplayContentIds,
        string iniCrc = "",
        string exeCrc = "")
    {
        ArgumentNullException.ThrowIfNull(clientKey);
        ArgumentNullException.ThrowIfNull(gameplayContentIds);

        // Length-prefixed so no client key or content id can blur the domain
        // boundary: "X\nY" with no content must hash differently from "X" with
        // content "Y".
        var canonical = clientKey.Length + "\n" + clientKey + "\n" + string.Join("\n", gameplayContentIds);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).Substring(0, FingerprintHashChars);
        if (string.IsNullOrEmpty(iniCrc) && string.IsNullOrEmpty(exeCrc))
        {
            return string.Join(
                OnlineConstants.FingerprintSeparator,
                OnlineConstants.ProfileFingerprintPrefix,
                clientKey,
                hash);
        }

        return string.Join(
            OnlineConstants.FingerprintSeparator,
            OnlineConstants.ProfileFingerprintV4Prefix,
            clientKey,
            hash,
            iniCrc ?? string.Empty,
            exeCrc ?? string.Empty);
    }

    /// <summary>
    /// Extracts the engine compatibility CRCs embedded in a versioned fingerprint.
    /// </summary>
    /// <param name="fingerprint">The fingerprint string.</param>
    /// <param name="iniCrc">The iniCRC or empty when absent.</param>
    /// <param name="exeCrc">The exeCRC or empty when absent.</param>
    /// <returns>True when the fingerprint carries the CRC segments.</returns>
    public static bool TryGetCompatibilityCrcs(string fingerprint, out string iniCrc, out string exeCrc)
    {
        iniCrc = string.Empty;
        exeCrc = string.Empty;
        if (string.IsNullOrEmpty(fingerprint))
        {
            return false;
        }

        var segments = fingerprint.Split(OnlineConstants.FingerprintSeparator);
        if (segments.Length != CompatibilitySegmentCount ||
            !string.Equals(segments[0], OnlineConstants.ProfileFingerprintV4Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        iniCrc = NormalizeCrc(segments[segments.Length - 2]);
        exeCrc = NormalizeCrc(segments[segments.Length - 1]);
        return true;
    }

    /// <summary>
    /// Decides whether two fingerprints confirm network compatibility
    /// through their embedded engine CRCs. In Generals/Zero Hour, multiplayer
    /// compatibility requires identical game rules (<c>iniCRC</c>) and
    /// compatible engine binaries (<c>exeCRC</c> match-or-absent).
    /// </summary>
    /// <param name="first">One fingerprint.</param>
    /// <param name="second">The other fingerprint.</param>
    /// <returns>True when both carry matching non-empty iniCRCs and matching-or-absent exeCRCs.</returns>
    public static bool CrcConfirmsCompatible(string first, string second)
    {
        if (!TryGetCompatibilityCrcs(first, out var firstIni, out var firstExe) ||
            !TryGetCompatibilityCrcs(second, out var secondIni, out var secondExe))
        {
            return false;
        }

        if (string.IsNullOrEmpty(firstIni) || string.IsNullOrEmpty(secondIni))
        {
            return false;
        }

        if (!string.Equals(firstIni, secondIni, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrEmpty(firstExe) ||
            string.IsNullOrEmpty(secondExe) ||
            string.Equals(firstExe, secondExe, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Bounds a gameplay content id list to the edge publish limit.
    /// </summary>
    /// <param name="contentIds">The sorted gameplay content ids.</param>
    /// <returns>At most the first 32 ids.</returns>
    public static IReadOnlyList<string> BoundContentIds(IReadOnlyList<string> contentIds)
    {
        ArgumentNullException.ThrowIfNull(contentIds);

        return contentIds.Count <= OnlineConstants.MaxExpectedContentIds
            ? contentIds
            : contentIds.Take(OnlineConstants.MaxExpectedContentIds).ToList();
    }

    /// <summary>
    /// Compares a local setup against the lobby's expected setup.
    /// In C&amp;C Generals/Zero Hour, compatibility is binary: matching INI CRC or
    /// matching fingerprint allows play; any difference is a mismatch.
    /// </summary>
    /// <param name="expectedFingerprint">The lobby's expected fingerprint.</param>
    /// <param name="expectedGameClientId">The lobby's expected game client key.</param>
    /// <param name="localFingerprint">The local profile fingerprint.</param>
    /// <param name="localGameClientId">The local game client key.</param>
    /// <returns>The match outcome (Exact or Mismatch).</returns>
    public static OnlineProfileMatch Compare(
        string expectedFingerprint,
        string expectedGameClientId,
        string localFingerprint,
        string localGameClientId)
    {
        if (string.IsNullOrEmpty(expectedFingerprint) || string.IsNullOrEmpty(localFingerprint))
        {
            return OnlineProfileMatch.Unknown;
        }

        if (string.Equals(expectedFingerprint, localFingerprint, StringComparison.Ordinal))
        {
            return OnlineProfileMatch.Exact;
        }

        // Matching engine INI CRCs confirm network compatibility regardless of packaging noise:
        // matching INI CRC means the setups produce identical game data and can play together.
        if (CrcConfirmsCompatible(expectedFingerprint, localFingerprint) &&
            AreGameTypesCompatible(expectedGameClientId, localGameClientId, expectedFingerprint, localFingerprint))
        {
            return OnlineProfileMatch.Exact;
        }

        return OnlineProfileMatch.Mismatch;
    }

    /// <summary>
    /// Compares a roster member's advertised fingerprint against the expected setup.
    /// </summary>
    /// <param name="memberFingerprint">The member's advertised fingerprint.</param>
    /// <param name="expectedFingerprint">The lobby's expected fingerprint.</param>
    /// <param name="expectedGameClientId">The lobby's expected game client key.</param>
    /// <returns>The match outcome (Exact or Mismatch).</returns>
    public static OnlineProfileMatch CompareMember(
        string memberFingerprint,
        string expectedFingerprint,
        string expectedGameClientId)
    {
        if (string.IsNullOrEmpty(memberFingerprint) || string.IsNullOrEmpty(expectedFingerprint))
        {
            return OnlineProfileMatch.Unknown;
        }

        if (string.Equals(memberFingerprint, expectedFingerprint, StringComparison.Ordinal))
        {
            return OnlineProfileMatch.Exact;
        }

        // Matching engine INI CRCs confirm network compatibility.
        if (CrcConfirmsCompatible(memberFingerprint, expectedFingerprint) &&
            AreGameTypesCompatible(expectedGameClientId, null, expectedFingerprint, memberFingerprint))
        {
            return OnlineProfileMatch.Exact;
        }

        return OnlineProfileMatch.Mismatch;
    }

    /// <summary>
    /// Extracts the game client key embedded in a fingerprint.
    /// Accepts current and retired fingerprint versions.
    /// </summary>
    /// <param name="fingerprint">The fingerprint string.</param>
    /// <param name="gameClientId">The game client key, or empty when unparsable.</param>
    /// <returns>True when the fingerprint has the expected shape.</returns>
    public static bool TryGetGameClientKey(string fingerprint, out string gameClientId)
    {
        gameClientId = string.Empty;
        if (string.IsNullOrEmpty(fingerprint))
        {
            return false;
        }

        var segments = fingerprint.Split(OnlineConstants.FingerprintSeparator);
        if (segments.Length < 3 || !IsKnownPrefix(segments[0]))
        {
            return false;
        }

        if (string.Equals(segments[0], OnlineConstants.ProfileFingerprintV4Prefix, StringComparison.Ordinal))
        {
            // Compatibility fingerprints append the content hash plus the
            // iniCRC/exeCRC after the client key; only the key segments
            // identify the client.
            if (segments.Length != CompatibilitySegmentCount)
            {
                return false;
            }

            gameClientId = string.Join(OnlineConstants.FingerprintSeparator, segments, 1, CompatibilitySegmentCount - 4);
            return !string.IsNullOrEmpty(gameClientId);
        }

        gameClientId = string.Join(OnlineConstants.FingerprintSeparator, segments, 1, segments.Length - 2);
        return !string.IsNullOrEmpty(gameClientId);
    }

    /// <summary>
    /// Counts how many gameplay content ids from the expected set are also present locally.
    /// </summary>
    /// <param name="expectedContentIds">The lobby's expected gameplay content ids.</param>
    /// <param name="localContentIds">The local profile's gameplay content ids.</param>
    /// <returns>The number of shared ids.</returns>
    public static int ScoreOverlap(IReadOnlyList<string> expectedContentIds, IReadOnlyList<string> localContentIds)
    {
        ArgumentNullException.ThrowIfNull(expectedContentIds);
        ArgumentNullException.ThrowIfNull(localContentIds);

        if (expectedContentIds.Count == 0 || localContentIds.Count == 0)
        {
            return 0;
        }

        var local = new HashSet<string>(localContentIds, StringComparer.Ordinal);
        return expectedContentIds.Count(id => local.Contains(id));
    }

    /// <summary>
    /// Determines whether two setups belong to the same base game (Generals vs Zero Hour).
    /// </summary>
    /// <param name="clientKeyA">The first setup's game client key.</param>
    /// <param name="clientKeyB">The second setup's game client key.</param>
    /// <param name="fingerprintA">The first setup's fingerprint fallback.</param>
    /// <param name="fingerprintB">The second setup's fingerprint fallback.</param>
    /// <returns><c>true</c> if both setups belong to the same base game; otherwise, <c>false</c>.</returns>
    public static bool AreGameTypesCompatible(
        string? clientKeyA,
        string? clientKeyB,
        string? fingerprintA,
        string? fingerprintB)
    {
        var gameTypeA = ExtractGameType(clientKeyA, fingerprintA);
        var gameTypeB = ExtractGameType(clientKeyB, fingerprintB);

        if (string.IsNullOrEmpty(gameTypeA) || string.IsNullOrEmpty(gameTypeB))
        {
            return true;
        }

        return string.Equals(gameTypeA, gameTypeB, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCrc(string? crc)
    {
        if (string.IsNullOrWhiteSpace(crc))
        {
            return string.Empty;
        }

        var trimmed = crc.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[2..];
        }

        return trimmed.ToUpperInvariant();
    }

    private static string ExtractGameType(string? clientKey, string? fingerprint)
    {
        if (!string.IsNullOrEmpty(clientKey))
        {
            var parts = clientKey.Split(OnlineConstants.FingerprintSeparator);
            if (parts.Length > 0 && !string.IsNullOrEmpty(parts[0]))
            {
                return parts[0];
            }
        }

        if (!string.IsNullOrEmpty(fingerprint) && TryGetGameClientKey(fingerprint, out var extracted))
        {
            var parts = extracted.Split(OnlineConstants.FingerprintSeparator);
            if (parts.Length > 0 && !string.IsNullOrEmpty(parts[0]))
            {
                return parts[0];
            }
        }

        return string.Empty;
    }

    private static bool IsKnownPrefix(string prefix) =>
        string.Equals(prefix, OnlineConstants.ProfileFingerprintPrefix, StringComparison.Ordinal) ||
        string.Equals(prefix, OnlineConstants.ProfileFingerprintV4Prefix, StringComparison.Ordinal) ||
        OnlineConstants.LegacyProfileFingerprintPrefixes.Contains(prefix, StringComparer.Ordinal);
}
