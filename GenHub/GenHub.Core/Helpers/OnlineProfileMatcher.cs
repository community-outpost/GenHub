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

        // Length-prefixed so no client key or content id can blur the domain
        // boundary: "X\nY" with no content must hash differently from "X" with
        // content "Y".
        var canonical = clientKey.Length + "\n" + clientKey + "\n" + string.Join("\n", gameplay);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return string.Join(
            OnlineConstants.FingerprintSeparator,
            OnlineConstants.ProfileFingerprintPrefix,
            clientKey,
            hash.Substring(0, FingerprintHashChars));
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
        OnlineConstants.LegacyProfileFingerprintPrefixes.Contains(prefix, StringComparer.Ordinal);
}
