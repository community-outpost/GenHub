using System;
using System.Collections.Generic;
using System.Linq;
using GenHub.Core.Models.Results.Content;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// Helper for swapping fields between variant search results while maintaining variant metadata.
/// </summary>
public static class VariantSwap
{
    private static readonly char[] NameDelimiters = [' ', '-', '_', '(', ')', '[', ']', '/', '\\', ',', '.'];

    /// <summary>
    /// Finds a variant matching the specified identifier using a multi-tier heuristic
    /// (exact ManifestId, exact Name, delimited ManifestId suffix, delimited identifier suffix,
    /// separator-stripped ManifestId suffix for synthesized IDs, and token/score-based Name matching).
    /// </summary>
    /// <param name="variants">The collection of candidate variants.</param>
    /// <param name="identifier">The variant manifest ID, identifier segment, or name to find.</param>
    /// <returns>The matching <see cref="InstallableVariant"/>, or null if no match was found.</returns>
    public static InstallableVariant? FindMatchingVariant(
        IEnumerable<InstallableVariant>? variants,
        string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || variants == null)
        {
            return null;
        }

        var list = variants as IList<InstallableVariant> ?? variants.ToList();

        return MatchByDirectIdentity(list, identifier)
            ?? MatchByManifestSuffix(list, identifier)
            ?? MatchByStrippedManifestId(list, identifier)
            ?? MatchByNameScore(list, identifier);
    }

    /// <summary>
    /// Creates an independent snapshot of a search result for variant dictionary storage.
    /// The card's own <see cref="ContentSearchResult"/> must not be stored by reference —
    /// in-place swaps would otherwise corrupt the default sibling entry.
    /// </summary>
    /// <param name="source">The search result to snapshot.</param>
    /// <returns>A shallow clone with copied resolver metadata and tags.</returns>
    public static ContentSearchResult Clone(ContentSearchResult source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var clone = new ContentSearchResult
        {
            Id = source.Id,
            Name = source.Name,
            Description = source.Description,
            Version = source.Version,
            ContentType = source.ContentType,
            IsInferred = source.IsInferred,
            TargetGame = source.TargetGame,
            ProviderName = source.ProviderName,
            AuthorName = source.AuthorName,
            IconUrl = source.IconUrl,
            BannerUrl = source.BannerUrl,
            LastUpdated = source.LastUpdated,
            DownloadSize = source.DownloadSize,
            DownloadCount = source.DownloadCount,
            Rating = source.Rating,
            IsInstalled = source.IsInstalled,
            HasUpdate = source.HasUpdate,
            RequiresResolution = source.RequiresResolution,
            ResolverId = source.ResolverId,
            SourceUrl = source.SourceUrl,
            SelectedDownloadUrl = source.SelectedDownloadUrl,
            Data = source.Data,
            ParsedPageData = source.ParsedPageData,
            VariantGroupId = source.VariantGroupId,
            VariantFamilyName = source.VariantFamilyName,
            Variants = source.Variants,
        };

        foreach (var url in source.ScreenshotUrls)
        {
            clone.ScreenshotUrls.Add(url);
        }

        foreach (var tag in source.Tags)
        {
            clone.Tags.Add(tag);
        }

        foreach (var kvp in source.Metadata)
        {
            clone.Metadata[kvp.Key] = kvp.Value;
        }

        foreach (var kvp in source.ResolverMetadata)
        {
            clone.ResolverMetadata[kvp.Key] = kvp.Value;
        }

        return clone;
    }

    /// <summary>
    /// Resolves the stable catalog identity used as the variant dictionary key.
    /// Prefers an explicit <see cref="ContentVariantInfo.ManifestId"/> when set; otherwise
    /// the sibling card's catalog <see cref="ContentSearchResult.Id"/>.
    /// </summary>
    /// <param name="sibling">The sibling search result for this variant.</param>
    /// <param name="info">Optional discoverer-supplied variant info.</param>
    /// <returns>A non-empty catalog key when available.</returns>
    public static string ResolveCatalogKey(ContentSearchResult sibling, ContentVariantInfo? info)
    {
        if (!string.IsNullOrEmpty(info?.ManifestId))
        {
            return info.ManifestId;
        }

        if (!string.IsNullOrEmpty(sibling.Id))
        {
            return sibling.Id;
        }

        return info?.Id ?? string.Empty;
    }

    /// <summary>
    /// Resolves a user-facing variant label that always distinguishes siblings
    /// (e.g. "weekly-2026-07-17 — Generals", "Control Bar Pro (Xezon) - 1080p (Recommended)"),
    /// never a stripped family-only or resolution-only title.
    /// </summary>
    /// <param name="sibling">The sibling search result for this variant.</param>
    /// <param name="info">Optional discoverer-supplied variant info.</param>
    /// <returns>The display name for dropdowns and card titles.</returns>
    public static string ResolveDisplayName(ContentSearchResult sibling, ContentVariantInfo? info)
    {
        if (!string.IsNullOrWhiteSpace(info?.Name))
        {
            return ResolveFromVariantInfo(sibling, info.Name);
        }

        if (!string.IsNullOrWhiteSpace(sibling.Name) &&
            (string.IsNullOrEmpty(sibling.VariantFamilyName) ||
             !string.Equals(sibling.Name, sibling.VariantFamilyName, StringComparison.Ordinal)))
        {
            return sibling.Name;
        }

        if (!string.IsNullOrWhiteSpace(sibling.VariantFamilyName) && !string.IsNullOrWhiteSpace(info?.Id))
        {
            var formattedSuffix = FormatFamilySuffix(sibling.VariantFamilyName, info.Id);
            if (formattedSuffix != null)
            {
                return formattedSuffix;
            }
        }

        return sibling.Name ?? sibling.Id ?? "Unknown";
    }

    /// <summary>
    /// Applies fields from the source variant search result onto the target search result.
    /// </summary>
    /// <param name="target">The target search result to mutate.</param>
    /// <param name="source">The source variant search result.</param>
    public static void Apply(ContentSearchResult target, ContentSearchResult source)
    {
        // Preserve variant grouping metadata on the card representative.
        var familyName = target.VariantFamilyName;
        var groupId = target.VariantGroupId;
        var variants = target.Variants;

        target.Id = source.Id;
        target.Name = source.Name;
        target.Description = source.Description;
        target.Version = source.Version;
        target.ContentType = source.ContentType;
        target.TargetGame = source.TargetGame;
        target.SelectedDownloadUrl = source.SelectedDownloadUrl;
        target.SourceUrl = source.SourceUrl;
        target.DownloadSize = source.DownloadSize;
        target.LastUpdated = source.LastUpdated;
        target.ParsedPageData = source.ParsedPageData;
        target.Data = source.Data;
        target.RequiresResolution = source.RequiresResolution;
        target.ResolverId = source.ResolverId;
        target.ProviderName = source.ProviderName;
        target.AuthorName = source.AuthorName;
        target.IconUrl = source.IconUrl;
        target.BannerUrl = source.BannerUrl;

        target.ScreenshotUrls.Clear();
        foreach (var url in source.ScreenshotUrls)
        {
            target.ScreenshotUrls.Add(url);
        }

        target.Metadata.Clear();
        foreach (var kvp in source.Metadata)
        {
            target.Metadata[kvp.Key] = kvp.Value;
        }

        target.ResolverMetadata.Clear();
        if (source.ResolverMetadata != null)
        {
            foreach (var kvp in source.ResolverMetadata)
            {
                target.ResolverMetadata[kvp.Key] = kvp.Value;
            }
        }

        target.VariantFamilyName = familyName;
        target.VariantGroupId = groupId;
        target.Variants = variants;
    }

    private static InstallableVariant? MatchByDirectIdentity(IList<InstallableVariant> list, string identifier)
    {
        // Tier 1: Exact ManifestId
        var match = list.FirstOrDefault(v => string.Equals(v.ManifestId, identifier, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            return match;
        }

        // Tier 2: Exact Name
        return list.FirstOrDefault(v => string.Equals(v.Name, identifier, StringComparison.OrdinalIgnoreCase));
    }

    private static InstallableVariant? MatchByManifestSuffix(IList<InstallableVariant> list, string identifier)
    {
        // Tier 3: Delimited ManifestId suffix
        var match = list.FirstOrDefault(v => !string.IsNullOrEmpty(v.ManifestId) &&
            (v.ManifestId.EndsWith($"-{identifier}", StringComparison.OrdinalIgnoreCase) ||
             v.ManifestId.EndsWith($".{identifier}", StringComparison.OrdinalIgnoreCase)));
        if (match != null)
        {
            return match;
        }

        // Tier 4: Delimited identifier suffix
        return list.FirstOrDefault(v => !string.IsNullOrEmpty(v.ManifestId) &&
            (identifier.EndsWith($"-{v.ManifestId}", StringComparison.OrdinalIgnoreCase) ||
             identifier.EndsWith($".{v.ManifestId}", StringComparison.OrdinalIgnoreCase)));
    }

    private static InstallableVariant? MatchByStrippedManifestId(IList<InstallableVariant> list, string identifier)
    {
        // Tier 5: Separator-stripped ManifestId suffix for synthesized variant IDs (anchored at segment boundaries).
        var idLastSegment = GetLastDotSegment(identifier);
        var cleanId = StripSeparators(idLastSegment);
        if (string.IsNullOrEmpty(cleanId))
        {
            return null;
        }

        var idPrefix = GetDotPrefix(identifier);

        return list
            .Where(v => !string.IsNullOrEmpty(v.ManifestId))
            .Select(v => new
            {
                Variant = v,
                CandidatePrefix = GetDotPrefix(v.ManifestId),
                CleanCandidate = StripSeparators(GetLastDotSegment(v.ManifestId)),
            })
            .Where(x => !string.IsNullOrEmpty(x.CleanCandidate) &&
                        (string.IsNullOrEmpty(idPrefix) || string.IsNullOrEmpty(x.CandidatePrefix) ||
                         string.Equals(idPrefix, x.CandidatePrefix, StringComparison.OrdinalIgnoreCase)) &&
                        x.CleanCandidate.EndsWith(cleanId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.CleanCandidate.Length - cleanId.Length)
            .Select(x => x.Variant)
            .FirstOrDefault();
    }

    private static string GetLastDotSegment(string input)
    {
        var lastDot = input.LastIndexOf('.');
        return lastDot >= 0 ? input[(lastDot + 1)..] : input;
    }

    private static string GetDotPrefix(string input)
    {
        var lastDot = input.LastIndexOf('.');
        return lastDot >= 0 ? input[..lastDot] : string.Empty;
    }

    private static InstallableVariant? MatchByNameScore(IList<InstallableVariant> list, string identifier)
    {
        InstallableVariant? bestNameMatch = null;
        int bestScore = 0;

        foreach (var v in list)
        {
            if (string.IsNullOrWhiteSpace(v.Name))
            {
                continue;
            }

            var score = CalculateNameMatchScore(v.Name, identifier);
            if (score == 3)
            {
                return v;
            }

            if (score > bestScore)
            {
                bestNameMatch = v;
                bestScore = score;
            }
        }

        return bestNameMatch;
    }

    private static int CalculateNameMatchScore(string name, string identifier)
    {
        var tokens = name.Split(NameDelimiters, StringSplitOptions.RemoveEmptyEntries);

        // Exact token match (highest priority: score 3)
        if (tokens.Any(t => string.Equals(t, identifier, StringComparison.OrdinalIgnoreCase)))
        {
            return 3;
        }

        // Token prefix match (e.g. "Russian" starts with "ru", but "Belarusian" does not: score 2)
        if (tokens.Any(t => t.StartsWith(identifier, StringComparison.OrdinalIgnoreCase)))
        {
            return 2;
        }

        // Name suffix match with word boundary (score 2)
        if (name.EndsWith($" {identifier}", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith($"- {identifier}", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        // Substring fallback only for identifiers of 3+ characters (score 1)
        if (identifier.Length >= 3 && name.Contains(identifier, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 0;
    }

    private static string ResolveFromVariantInfo(ContentSearchResult sibling, string infoName)
    {
        if (!string.IsNullOrWhiteSpace(sibling.Name) &&
            !string.IsNullOrEmpty(sibling.VariantFamilyName) &&
            !string.Equals(sibling.Name, sibling.VariantFamilyName, StringComparison.Ordinal) &&
            sibling.Name.Contains(infoName, StringComparison.OrdinalIgnoreCase))
        {
            return sibling.Name;
        }

        if (!string.IsNullOrWhiteSpace(sibling.VariantFamilyName) &&
            !infoName.Contains(sibling.VariantFamilyName, StringComparison.OrdinalIgnoreCase))
        {
            return $"{sibling.VariantFamilyName} - {infoName}";
        }

        return infoName;
    }

    private static string? FormatFamilySuffix(string familyName, string infoId)
    {
        var suffix = infoId.Contains('.') ? infoId[(infoId.LastIndexOf('.') + 1)..] : infoId;
        if (!string.IsNullOrWhiteSpace(suffix) &&
            !string.Equals(suffix, familyName, StringComparison.OrdinalIgnoreCase))
        {
            return $"{familyName} — {suffix}";
        }

        return null;
    }

    private static string StripSeparators(string input)
    {
        Span<char> buffer = input.Length <= 128 ? stackalloc char[input.Length] : new char[input.Length];
        int count = 0;
        foreach (char c in input)
        {
            if (char.IsLetterOrDigit(c))
            {
                buffer[count++] = char.ToLowerInvariant(c);
            }
        }

        return new string(buffer[..count]);
    }
}
