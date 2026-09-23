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
        ContentType.GameClient => true,
        ContentType.GameInstallation => true,
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
    /// Lists the profile's gameplay content ids in stable order. Content with
    /// an unknown type counts as gameplay: an unrecognized id must surface as
    /// a mismatch, never as a silent match.
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
            .Where(id => contentTypes is null || !contentTypes.TryGetValue(id, out var type) || IsGameplayContent(type))
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

        iniCrc = segments[segments.Length - 2];
        exeCrc = segments[segments.Length - 1];
        return true;
    }

    /// <summary>
    /// Decides whether two fingerprints confirm byte-level compatibility
    /// through their embedded engine CRCs. CRC evidence only ever confirms:
    /// equal CRCs upgrade a same-client verdict to exact, while differing or
    /// absent CRCs leave the id-based verdict untouched.
    /// </summary>
    /// <param name="first">One fingerprint.</param>
    /// <param name="second">The other fingerprint.</param>
    /// <returns>True when both carry equal iniCRCs with no conflicting exeCRC.</returns>
    public static bool CrcConfirmsCompatible(string first, string second)
    {
        if (!TryGetCompatibilityCrcs(first, out var firstIni, out var firstExe) ||
            !TryGetCompatibilityCrcs(second, out var secondIni, out var secondExe))
        {
            return false;
        }

        if (string.IsNullOrEmpty(firstIni) || !string.Equals(firstIni, secondIni, StringComparison.Ordinal))
        {
            return false;
        }

        return string.IsNullOrEmpty(firstExe) ||
            string.IsNullOrEmpty(secondExe) ||
            string.Equals(firstExe, secondExe, StringComparison.Ordinal);
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
    /// </summary>
    /// <param name="expectedFingerprint">The lobby's expected fingerprint.</param>
    /// <param name="expectedGameClientId">The lobby's expected game client key.</param>
    /// <param name="localFingerprint">The local profile fingerprint.</param>
    /// <param name="localGameClientId">The local game client key.</param>
    /// <returns>The match outcome.</returns>
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

        if (!string.IsNullOrEmpty(expectedGameClientId) &&
            string.Equals(expectedGameClientId, localGameClientId, StringComparison.Ordinal))
        {
            // Equal engine CRCs overrule id-level noise: the setups produce
            // the same game data, so they can play together.
            if (CrcConfirmsCompatible(expectedFingerprint, localFingerprint))
            {
                return OnlineProfileMatch.Exact;
            }

            return OnlineProfileMatch.SameClient;
        }

        return OnlineProfileMatch.Mismatch;
    }

    /// <summary>
    /// Compares a roster member's advertised fingerprint against the expected setup.
    /// </summary>
    /// <param name="memberFingerprint">The member's advertised fingerprint.</param>
    /// <param name="expectedFingerprint">The lobby's expected fingerprint.</param>
    /// <param name="expectedGameClientId">The lobby's expected game client key.</param>
    /// <returns>The match outcome.</returns>
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

        if (!string.IsNullOrEmpty(expectedGameClientId) &&
            TryGetGameClientKey(memberFingerprint, out var memberClient) &&
            string.Equals(memberClient, expectedGameClientId, StringComparison.Ordinal))
        {
            // Equal engine CRCs overrule id-level noise: the setups produce
            // the same game data, so they can play together.
            if (CrcConfirmsCompatible(memberFingerprint, expectedFingerprint))
            {
                return OnlineProfileMatch.Exact;
            }

            return OnlineProfileMatch.SameClient;
        }

        return OnlineProfileMatch.Mismatch;
    }

    /// <summary>
    /// Extracts the game client key embedded in a fingerprint.
    /// Accepts current and retired fingerprint versions so mixed-version
    /// lobbies still detect the same-client case.
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
    /// Scores how much of the expected gameplay content a local profile covers.
    /// Used to rank same-client candidates when no exact match exists.
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

    private static bool IsKnownPrefix(string prefix) =>
        string.Equals(prefix, OnlineConstants.ProfileFingerprintPrefix, StringComparison.Ordinal) ||
        string.Equals(prefix, OnlineConstants.ProfileFingerprintV4Prefix, StringComparison.Ordinal) ||
        OnlineConstants.LegacyProfileFingerprintPrefixes.Contains(prefix, StringComparer.Ordinal);
}
