using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Resolves Community Outpost content into manifests.
/// Supports the GenPatcher dl.dat catalog format with multiple download mirrors.
/// Uses <see cref="GenPatcherContentRegistry"/> for content metadata.
/// </summary>
/// <param name="manifestBuilderFactory">Factory to create new manifest builders per resolve operation.</param>
/// <param name="providerLoader">Provider definition loader for endpoint configuration.</param>
/// <param name="logger">The logger.</param>
public class CommunityOutpostResolver(
    Func<IContentManifestBuilder> manifestBuilderFactory,
    IProviderDefinitionLoader providerLoader,
    ILogger<CommunityOutpostResolver> logger) : IContentResolver
{
    /// <inheritdoc/>
    public string ResolverId => CommunityOutpostConstants.PublisherId;

    /// <inheritdoc/>
    public Task<OperationResult<ContentManifest>> ResolveAsync(
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken = default)
    {
        // Call the provider-aware overload with null (uses defaults from constants)
        return ResolveAsync(provider: null, discoveredItem, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<OperationResult<ContentManifest>> ResolveAsync(
        ProviderDefinition? provider,
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation(
                "Resolving Community Outpost content: {Name} v{Version}",
                discoveredItem.Name,
                discoveredItem.Version);

            // Get provider definition if not provided
            provider ??= providerLoader.GetProvider(CommunityOutpostConstants.PublisherId);
            if (provider == null)
            {
                return Task.FromResult(OperationResult<ContentManifest>.CreateFailure(
                    $"Provider definition '{CommunityOutpostConstants.PublisherId}' not found. Ensure communityoutpost.provider.json exists."));
            }

            // Get configuration from provider definition
            var websiteUrl = provider.Endpoints.WebsiteUrl ?? provider.Endpoints.GetEndpoint("websiteUrl") ?? string.Empty;
            var patchPageUrl = provider.Endpoints.GetEndpoint("patchPageUrl") ?? string.Empty;

            logger.LogDebug(
                "Using endpoints - WebsiteUrl: {WebsiteUrl}, PatchPageUrl: {PatchPageUrl}",
                websiteUrl,
                patchPageUrl);

            // Extract metadata from resolver metadata (set by the discoverer/parser)
            var contentCode = GetMetadataValue(discoveredItem, "contentCode", "unknown");
            var category = GetMetadataValue(discoveredItem, "category", "Other");
            var fileSize = GetMetadataValueLong(discoveredItem, "fileSize", 0);

            // Get content metadata from GenPatcherContentRegistry (static, hardcoded metadata)
            var contentMetadata = GenPatcherContentRegistry.GetMetadata(contentCode);

            // Determine filename from URL or content code
            var downloadUrl = discoveredItem.SourceUrl ?? throw new InvalidOperationException(
                "SourceUrl cannot be null for Community Outpost content");

            var filename = ExtractFileName(new Uri(downloadUrl), contentCode);

            // Get all mirror URLs for fallback support
            var mirrorUrls = GetMirrorUrls(discoveredItem);

            logger.LogDebug(
                "Resolving content code {Code} with {MirrorCount} mirrors, file size: {Size} bytes",
                contentCode,
                mirrorUrls.Count,
                fileSize);

            // Generate a deterministic content name from the content code.
            // Preserve a catalog variant suffix (e.g. cbpr-1080p) so the factory builds
            // only the selected resolution instead of every variant.
            var contentName = GenerateContentName(contentCode, contentMetadata);
            var requestedVariantSuffix = TryExtractVariantSuffix(discoveredItem, contentMetadata);
            if (!string.IsNullOrEmpty(requestedVariantSuffix) &&
                contentName.IndexOf('-') < 0)
            {
                contentName = $"{contentCode}-{requestedVariantSuffix}".ToLowerInvariant();
            }

            var manifestVersion = DetermineManifestVersion(discoveredItem, contentMetadata);

            logger.LogDebug(
                "Generating manifest ID: Publisher={Publisher}, ContentType={ContentType}, ContentName={ContentName}, Version={Version}",
                CommunityOutpostConstants.PublisherType,
                contentMetadata.ContentType,
                contentName,
                manifestVersion);

            var manifestBuilder = manifestBuilderFactory();
            var manifest = BuildBaseManifest(
                manifestBuilder,
                discoveredItem,
                contentMetadata,
                contentName,
                manifestVersion,
                websiteUrl,
                patchPageUrl);

            manifest.AddRemoteFileAsync(
                filename,
                downloadUrl,
                ContentSourceType.RemoteDownload,
                isExecutable: false).Wait(cancellationToken);

            var builtManifest = manifest.Build();

            ApplyPostResolutionMetadata(
                builtManifest,
                new PostResolutionContext(
                    discoveredItem,
                    contentMetadata,
                    contentCode,
                    filename,
                    requestedVariantSuffix,
                    mirrorUrls,
                    fileSize));

            logger.LogInformation(
                "Successfully resolved Community Outpost manifest: {ManifestId} for {ContentCode} ({Category})",
                builtManifest.Id,
                contentCode,
                category);

            return Task.FromResult(OperationResult<ContentManifest>.CreateSuccess(builtManifest));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve Community Outpost content: {Name}", discoveredItem.Name);
            return Task.FromResult(OperationResult<ContentManifest>.CreateFailure(
                $"Failed to resolve content: {ex.Message}"));
        }
    }

    /// <summary>
    /// Gets the filename from the download URI or generates one from the content code.
    /// </summary>
    private static string ExtractFileName(Uri downloadUri, string contentCode)
    {
        var lastSegment = downloadUri.Segments.Length > 0 ? downloadUri.Segments[^1].Trim('/') : string.Empty;

        if (!string.IsNullOrEmpty(lastSegment) && lastSegment.Contains('.'))
        {
            return lastSegment;
        }

        return $"{contentCode}{CommunityOutpostConstants.DatFileExtension}";
    }

    /// <summary>
    /// Extracts a numeric version suitable for manifest ID.
    /// </summary>
    private static string ExtractManifestVersion(string version)
    {
        if (string.IsNullOrEmpty(version))
        {
            return "0";
        }

        var trimmed = version.Trim();
        if (trimmed == "1.0" || trimmed == "1.0.0" || trimmed == "0")
        {
            return "0";
        }

        // Handle date versions like "2025-11-07" (YYYY-MM-DD)
        if (trimmed.Length == 10 && trimmed[4] == '-' && trimmed[7] == '-')
        {
            var dateDigits = trimmed.Replace("-", string.Empty);
            if (dateDigits.Length == 8 && int.TryParse(dateDigits, out var dateValue))
            {
                return dateValue.ToString();
            }
        }

        // Handle date versions like "13-02-2025" (DD-MM-YYYY)
        if (trimmed.Length == 10 && trimmed[2] == '-' && trimmed[5] == '-')
        {
            // Reorder to YYYYMMDD
            var parts = trimmed.Split('-');
            if (parts.Length == 3)
            {
                var dateDigits = $"{parts[2]}{parts[1]}{parts[0]}";
                if (dateDigits.Length == 8 && int.TryParse(dateDigits, out var dateValue))
                {
                    return dateValue.ToString();
                }
            }
        }

        // Remove dots and leading zeros to get numeric version
        var digits = trimmed.Replace(".", string.Empty);

        if (int.TryParse(digits, out var numericVersion))
        {
            return numericVersion.ToString();
        }

        return "0";
    }

    /// <summary>
    /// Builds the tags list for the manifest.
    /// </summary>
    private static List<string> BuildTags(ContentSearchResult item, GenPatcherContentMetadata metadata)
    {
        var tags = new List<string>(item.Tags);

        if (!string.IsNullOrEmpty(metadata.LanguageCode))
        {
            tags.Add(metadata.LanguageCode);
        }

        tags.Add(metadata.Category.ToString().ToLowerInvariant());

        return tags;
    }

    /// <summary>
    /// Gets a metadata value from the search result.
    /// </summary>
    private static string GetMetadataValue(ContentSearchResult item, string key, string defaultValue)
    {
        if (item.ResolverMetadata?.TryGetValue(key, out var value) == true)
        {
            return value;
        }

        return defaultValue;
    }

    /// <summary>
    /// Gets a long metadata value from the search result.
    /// </summary>
    private static long GetMetadataValueLong(ContentSearchResult item, string key, long defaultValue)
    {
        var stringValue = GetMetadataValue(item, key, string.Empty);
        return long.TryParse(stringValue, out var result) ? result : defaultValue;
    }

    private string DetermineManifestVersion(
        ContentSearchResult discoveredItem,
        GenPatcherContentMetadata contentMetadata)
    {
        var idParts = discoveredItem.Id?.Split('.') ?? [];
        if (idParts.Length >= 5 && int.TryParse(idParts[1], out _))
        {
            return idParts[1];
        }

        var versionSource = !string.IsNullOrEmpty(contentMetadata.Version)
            ? contentMetadata.Version
            : discoveredItem.Version;
        return ExtractManifestVersion(versionSource);
    }

    private IContentManifestBuilder BuildBaseManifest(
        IContentManifestBuilder manifestBuilder,
        ContentSearchResult discoveredItem,
        GenPatcherContentMetadata contentMetadata,
        string contentName,
        string manifestVersion,
        string websiteUrl,
        string patchPageUrl)
    {
        var manifest = manifestBuilder
            .WithBasicInfo(
                CommunityOutpostConstants.PublisherType,
                contentName,
                manifestVersion)
            .WithContentType(contentMetadata.ContentType, contentMetadata.TargetGame)
            .WithPublisher(
                name: CommunityOutpostConstants.PublisherName,
                website: websiteUrl,
                supportUrl: patchPageUrl,
                contactEmail: string.Empty,
                publisherType: CommunityOutpostConstants.PublisherType)
            .WithMetadata(
                contentMetadata.Description,
                tags: BuildTags(discoveredItem, contentMetadata),
                changelogUrl: patchPageUrl)
            .WithInstallationInstructions(WorkspaceConstants.DefaultWorkspaceStrategy);

        var dependencies = contentMetadata.GetDependencies();
        foreach (var dependency in dependencies)
        {
            manifest.AddDependency(
                id: dependency.Id,
                name: dependency.Name,
                dependencyType: dependency.DependencyType,
                installBehavior: dependency.InstallBehavior,
                minVersion: dependency.MinVersion ?? string.Empty,
                maxVersion: dependency.MaxVersion ?? string.Empty,
                compatibleVersions: dependency.CompatibleVersions,
                isExclusive: GenPatcherDependencyBuilder.IsCategoryExclusive(contentMetadata.Category),
                conflictsWith: dependency.ConflictsWith);
        }

        return manifest;
    }

    private readonly record struct PostResolutionContext(
        ContentSearchResult DiscoveredItem,
        GenPatcherContentMetadata ContentMetadata,
        string ContentCode,
        string Filename,
        string? RequestedVariantSuffix,
        List<string> MirrorUrls,
        long FileSize);

    private static void ApplyPostResolutionMetadata(ContentManifest builtManifest, in PostResolutionContext context)
    {
        builtManifest.InstallationInstructions ??= new InstallationInstructions();
        builtManifest.Metadata ??= new ContentMetadata();

        ApplyMetadataTags(builtManifest, context);
        ApplyManifestFileConfig(builtManifest, context);

        builtManifest.Name = context.ContentMetadata.SupportsVariants && !string.IsNullOrEmpty(context.ContentMetadata.DisplayName)
            ? context.ContentMetadata.DisplayName
            : context.DiscoveredItem.Name ?? context.ContentMetadata.DisplayName;

        builtManifest.Version = ResolveManifestVersion(context.ContentCode, context.ContentMetadata, context.DiscoveredItem);
    }

    private static void ApplyMetadataTags(ContentManifest builtManifest, in PostResolutionContext context)
    {
        builtManifest.Metadata ??= new ContentMetadata();
        builtManifest.Metadata.Tags ??= [];

        if (!string.IsNullOrEmpty(context.RequestedVariantSuffix))
        {
            builtManifest.Metadata.SelectedVariantId = context.RequestedVariantSuffix;
            builtManifest.Metadata.Tags.Add($"requestedVariant:{context.RequestedVariantSuffix}");
            builtManifest.Metadata.Tags.Add($"selectedVariant:{context.RequestedVariantSuffix}");
            builtManifest.Metadata.Tags.Add($"variant:{context.RequestedVariantSuffix}");
        }

        if (context.MirrorUrls.Count > 1)
        {
            builtManifest.Metadata.Tags.Add($"mirrors:{context.MirrorUrls.Count}");
        }

        builtManifest.Metadata.Tags.Add($"contentCode:{context.ContentCode}");
        builtManifest.Metadata.Tags.Add($"installTarget:{context.ContentMetadata.InstallTarget}");
    }

    private static void ApplyManifestFileConfig(ContentManifest builtManifest, in PostResolutionContext context)
    {
        if (context.Filename.EndsWith(CommunityOutpostConstants.DatFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var file in builtManifest.Files)
            {
                if (file.RelativePath == context.Filename)
                {
                    file.SourcePath = "archive:7z";
                    file.InstallTarget = context.ContentMetadata.InstallTarget;
                }
            }
        }

        if (context.FileSize > 0 && builtManifest.Files.Count > 0)
        {
            builtManifest.Files[0].Size = context.FileSize;
        }
    }

    private static string ResolveManifestVersion(string contentCode, GenPatcherContentMetadata contentMetadata, ContentSearchResult discoveredItem)
    {
        if (contentCode == "community-patch" && !string.IsNullOrWhiteSpace(discoveredItem.Version))
        {
            return discoveredItem.Version;
        }

        if (!string.IsNullOrWhiteSpace(contentMetadata.Version))
        {
            return contentMetadata.Version;
        }

        if (!string.IsNullOrWhiteSpace(discoveredItem.Version))
        {
            return discoveredItem.Version;
        }

        return CommunityOutpostCatalogConstants.DefaultMetadataVersion;
    }

    /// <summary>
    /// Generates a deterministic content name for manifest ID generation.
    /// </summary>
    private string GenerateContentName(string contentCode, GenPatcherContentMetadata metadata)
    {
        // For official patches like "104p" -> "patch104polish"
        if (metadata.Category == GenPatcherContentCategory.OfficialPatch && !string.IsNullOrEmpty(metadata.LanguageCode))
        {
            var languageName = GetLanguageDisplayName(metadata.LanguageCode);
            var codePrefix = contentCode.Length >= 3 ? contentCode[..3] : contentCode;
            return $"patch{codePrefix}{languageName}".ToLowerInvariant();
        }

        // For content with language codes, append language
        if (!string.IsNullOrEmpty(metadata.LanguageCode))
        {
            var languageName = GetLanguageDisplayName(metadata.LanguageCode);
            return $"{contentCode}{languageName}".ToLowerInvariant();
        }

        // For other content, use the content code directly
        return contentCode.ToLowerInvariant();
    }

    /// <summary>
    /// Gets a display name for a language code.
    /// </summary>
    private string GetLanguageDisplayName(string languageCode)
    {
        return languageCode.ToLowerInvariant() switch
        {
            "en" => "english",
            "de" => "german",
            "de-alt" => "german2",
            "fr" => "french",
            "es" => "spanish",
            "it" => "italian",
            "pt-br" => "brazilian",
            "zh" => "chinese",
            "ko" => "korean",
            "pl" => "polish",
            _ => languageCode.ToLowerInvariant(),
        };
    }

    /// <summary>
    /// Extracts a variant suffix from a search result ID, metadata, or name (e.g. cbpr-1080p -> 1080p).
    /// </summary>
    private string? TryExtractVariantSuffix(ContentSearchResult item, GenPatcherContentMetadata metadata)
    {
        // 1. Check ResolverMetadata
        if (item.ResolverMetadata != null)
        {
            if (item.ResolverMetadata.TryGetValue("selectedVariant", out var selectedVariant) && !string.IsNullOrWhiteSpace(selectedVariant))
            {
                return selectedVariant.Trim();
            }

            if (item.ResolverMetadata.TryGetValue("requestedVariant", out var requestedVariant) && !string.IsNullOrWhiteSpace(requestedVariant))
            {
                return requestedVariant.Trim();
            }

            if (item.ResolverMetadata.TryGetValue("variant", out var variant) && !string.IsNullOrWhiteSpace(variant))
            {
                return variant.Trim();
            }
        }

        // 2. Check Id segment (e.g. 1.0.communityoutpost.addon.cbpr-1080p -> 1080p)
        if (!string.IsNullOrEmpty(item.Id))
        {
            var parts = item.Id.Split('.');
            var contentName = parts.Length >= 5 ? parts[4] : item.Id;
            var dashIndex = contentName.IndexOf('-');
            if (dashIndex > 0 && dashIndex < contentName.Length - 1)
            {
                return contentName[(dashIndex + 1)..];
            }

            if (metadata.Variants is { Count: > 0 })
            {
                var matchingVariant = metadata.Variants.FirstOrDefault(v =>
                    contentName.EndsWith(v.Id, StringComparison.OrdinalIgnoreCase) ||
                    contentName.EndsWith(v.Id.Replace("-", string.Empty), StringComparison.OrdinalIgnoreCase));
                if (matchingVariant != null)
                {
                    return matchingVariant.Id;
                }
            }
        }

        // 3. Check Name against known variants
        if (!string.IsNullOrEmpty(item.Name) && metadata.Variants is { Count: > 0 })
        {
            var matchingVariant = metadata.Variants.FirstOrDefault(v =>
                item.Name.EndsWith(v.Name, StringComparison.OrdinalIgnoreCase) ||
                item.Name.Contains(v.Name, StringComparison.OrdinalIgnoreCase) ||
                item.Name.EndsWith(v.Id, StringComparison.OrdinalIgnoreCase));
            if (matchingVariant != null)
            {
                return matchingVariant.Id;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the list of mirror URLs from the search result metadata.
    /// </summary>
    private List<string> GetMirrorUrls(ContentSearchResult item)
    {
        var mirrorUrlsJson = GetMetadataValue(item, "mirrorUrls", "[]");

        try
        {
            return JsonSerializer.Deserialize<List<string>>(mirrorUrlsJson) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to deserialize mirror URLs");
            return [];
        }
    }
}