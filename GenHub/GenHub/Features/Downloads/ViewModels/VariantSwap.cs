using System;
using GenHub.Core.Models.Results.Content;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// Helper for swapping fields between variant search results while maintaining variant metadata.
/// </summary>
public static class VariantSwap
{
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
}
