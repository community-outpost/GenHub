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
    private sealed record ManifestMetadataContext(
        ContentSearchResult DiscoveredItem,
        GenPatcherContentMetadata ContentMetadata,
        string ContentCode,
        string Filename,
        IReadOnlyList<string> MirrorUrls,
        long FileSize,
        string ManifestVersion);

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
            if (string.IsNullOrEmpty(discoveredItem.SourceUrl))
            {
                return Task.FromResult(OperationResult<ContentManifest>.CreateFailure(
                    "SourceUrl cannot be null or empty for Community Outpost content"));
            }

            var downloadUrl = discoveredItem.SourceUrl;

            var filename = Uri.TryCreate(downloadUrl, UriKind.Absolute, out var parsedUri)
                ? ExtractFileName(parsedUri, contentCode)
                : $"{contentCode}{CommunityOutpostConstants.DatFileExtension}";

            // Get all mirror URLs for fallback support
            var mirrorUrls = GetMirrorUrls(discoveredItem);

            logger.LogDebug(
                "Resolving content code {Code} with {MirrorCount} mirrors, file size: {Size} bytes",
                contentCode,
                mirrorUrls.Count,
                fileSize);

            // Generate a deterministic content name from the content code
            var contentName = GenerateContentName(contentCode, contentMetadata);

            // Extract version number for manifest ID
            var versionSource = !string.IsNullOrEmpty(contentMetadata.Version)
                ? contentMetadata.Version
                : discoveredItem.Version;
            var manifestVersion = ExtractManifestVersion(versionSource);

            logger.LogDebug(
                "Generating manifest ID: Publisher={Publisher}, ContentType={ContentType}, ContentName={ContentName}, Version={Version}",
                CommunityOutpostConstants.PublisherType,
                contentMetadata.ContentType,
                contentName,
                manifestVersion);

            // Create a new manifest builder for each resolve operation to ensure clean state
            var manifestBuilder = manifestBuilderFactory();

            // Build manifest with correct parameters
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

            // Add dependencies based on content type and category
            PopulateDependencies(manifest, contentMetadata, contentCode);

            // Add the file as a remote download
            manifest.AddRemoteFileAsync(
                filename,
                downloadUrl,
                ContentSourceType.RemoteDownload,
                isExecutable: false).Wait(cancellationToken);

            // Store additional metadata in the manifest for the deliverer
            var builtManifest = manifest.Build();
            ApplyBuiltManifestMetadata(
                builtManifest,
                new ManifestMetadataContext(
                    discoveredItem,
                    contentMetadata,
                    contentCode,
                    filename,
                    mirrorUrls,
                    fileSize,
                    manifestVersion));

            logger.LogInformation(
                "Successfully resolved Community Outpost manifest: {ManifestId} for {ContentCode} ({Category})",
                builtManifest.Id,
                contentCode,
                category);

            return Task.FromResult(OperationResult<ContentManifest>.CreateSuccess(builtManifest));
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to resolve Community Outpost content: {Name}",
                discoveredItem.Name);
            return Task.FromResult(OperationResult<ContentManifest>.CreateFailure(
                $"Failed to resolve content '{discoveredItem.Name}': {ex.Message}"));
        }
    }

    private static void ApplyBuiltManifestMetadata(
        ContentManifest builtManifest,
        ManifestMetadataContext context)
    {
        builtManifest.ManifestVersion = context.ManifestVersion;
        builtManifest.InstallationInstructions ??= new InstallationInstructions();
        builtManifest.Metadata ??= new ContentMetadata();

        builtManifest.Metadata.Tags ??= [];
        if (context.MirrorUrls.Count > 1)
        {
            builtManifest.Metadata.Tags.Add($"mirrors:{context.MirrorUrls.Count}");
        }

        builtManifest.Metadata.Tags.Add($"contentCode:{context.ContentCode}");
        builtManifest.Metadata.Tags.Add($"installTarget:{context.ContentMetadata.InstallTarget}");

        if (context.Filename.EndsWith(CommunityOutpostConstants.DatFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var file in builtManifest.Files.Where(f => f.RelativePath == context.Filename))
            {
                file.SourcePath = "archive:7z";
                file.InstallTarget = context.ContentMetadata.InstallTarget;
            }
        }

        if (context.FileSize > 0 && builtManifest.Files.Count > 0)
        {
            builtManifest.Files[0].Size = context.FileSize;
        }

        builtManifest.Name = context.DiscoveredItem.Name ?? context.ContentMetadata.DisplayName;

        if (context.ContentCode == "community-patch" && !string.IsNullOrEmpty(context.DiscoveredItem.Version))
        {
            builtManifest.Version = context.DiscoveredItem.Version;
        }
        else
        {
            builtManifest.Version = !string.IsNullOrEmpty(context.ContentMetadata.Version)
                ? context.ContentMetadata.Version
                : context.DiscoveredItem.Version;
        }
    }

    /// <summary>
    /// Generates a deterministic content name for manifest ID generation.
    /// </summary>
    private static string GenerateContentName(string contentCode, GenPatcherContentMetadata metadata)
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
    private static string GetLanguageDisplayName(string languageCode)
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
    /// Extracts a numeric version suitable for manifest ID.
    /// </summary>
    private static string ExtractManifestVersion(string version)
    {
        if (string.IsNullOrEmpty(version))
        {
            return "0";
        }

        // Handle date versions like "2025-11-07" (YYYY-MM-DD)
        if (version.Length == 10 && version[4] == '-' && version[7] == '-')
        {
            var dateDigits = version.Replace("-", string.Empty);
            if (dateDigits.Length == 8 && int.TryParse(dateDigits, out var dateValue))
            {
                return dateValue.ToString();
            }
        }

        // Handle date versions like "13-02-2025" (DD-MM-YYYY)
        if (version.Length == 10 && version[2] == '-' && version[5] == '-')
        {
            // Reorder to YYYYMMDD
            var parts = version.Split('-');
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
        var digits = version.Replace(".", string.Empty);

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
        if (item.ResolverMetadata is { } metadata && metadata.TryGetValue(key, out var value))
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

    /// <summary>
    /// Gets the filename from the download URI or generates one from the content code.
    /// </summary>
    /// <param name="uri">The download URI.</param>
    /// <param name="contentCode">The content code.</param>
    /// <returns>The extracted or generated filename.</returns>
    private static string ExtractFileName(Uri uri, string contentCode)
    {
        try
        {
            var path = uri.AbsolutePath;
            var lastSegment = path.Split('/')[^1];

            if (!string.IsNullOrEmpty(lastSegment) && lastSegment.Contains('.'))
            {
                return lastSegment;
            }
        }
        catch
        {
            // Fall through to default filename
        }

        return $"{contentCode}{CommunityOutpostConstants.DatFileExtension}";
    }

    private void PopulateDependencies(
        IContentManifestBuilder manifest,
        GenPatcherContentMetadata contentMetadata,
        string contentCode)
    {
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

            logger.LogDebug(
                "Added dependency {DepName} ({DepType}) to manifest for {ContentCode}",
                dependency.Name,
                dependency.DependencyType,
                contentCode);
        }
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
