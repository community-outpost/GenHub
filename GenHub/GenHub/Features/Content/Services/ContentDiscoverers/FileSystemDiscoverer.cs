using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Manifest;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.ContentDiscoverers;

/// <summary>
/// Discovers content from local file system locations.
/// Integrates with ManifestDiscoveryService for comprehensive manifest discovery.
/// </summary>
public class FileSystemDiscoverer : IContentDiscoverer
{
    private readonly List<string> _contentDirectories = [];
    private readonly ILogger<FileSystemDiscoverer> _logger;
    private readonly ManifestDiscoveryService _manifestDiscoveryService;
    private readonly IConfigurationProviderService _configurationProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemDiscoverer"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="manifestDiscoveryService">The manifest discovery service.</param>
    /// <param name="configurationProvider">The unified configuration provider.</param>
    public FileSystemDiscoverer(
        ILogger<FileSystemDiscoverer> logger,
        ManifestDiscoveryService manifestDiscoveryService,
        IConfigurationProviderService configurationProvider)
    {
        _logger = logger;
        _manifestDiscoveryService = manifestDiscoveryService;
        _configurationProvider = configurationProvider;

        InitializeContentDirectories();
    }

    private static bool MatchesQuery(ContentManifest manifest, ContentSearchQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.SearchTerm) &&
            !manifest.Name.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase) &&
            !manifest.Id.Value.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (query.ContentType.HasValue && manifest.ContentType != query.ContentType.Value)
        {
            return false;
        }

        if (query.TargetGame.HasValue && manifest.TargetGame != query.TargetGame.Value)
        {
            return false;
        }

        return true;
    }

    /// <inheritdoc />
    public string SourceName => "Local File System";

    /// <inheritdoc />
    public string Description => "Discovers content from local file system with integrated manifest discovery.";

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.DirectSearch |
        ContentSourceCapabilities.SupportsManifestGeneration;

    /// <inheritdoc />
    public async Task<OperationResult<ContentDiscoveryResult>> DiscoverAsync(
        ContentSearchQuery query, CancellationToken cancellationToken = default)
    {
        var discoveredItems = new List<ContentSearchResult>();

        // Use ManifestDiscoveryService for comprehensive discovery
        Dictionary<string, ContentManifest> discoveredManifests;
        try
        {
            discoveredManifests = await _manifestDiscoveryService.DiscoverManifestsAsync(_contentDirectories, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover manifests from content directories");
            return OperationResult<ContentDiscoveryResult>.CreateFailure($"Failed to discover manifests {ex.Message}");
        }

        foreach (var manifestEntry in discoveredManifests)
        {
            var manifest = manifestEntry.Value;

            if (MatchesQuery(manifest, query))
            {
                var discovered = new ContentSearchResult
                {
                    Id = manifest.Id,
                    Name = manifest.Name,
                    Description = manifest.Metadata?.Description ?? string.Empty,
                    Version = manifest.Version,
                    ContentType = manifest.ContentType,
                    TargetGame = manifest.TargetGame,
                    ProviderName = SourceName,
                    AuthorName = manifest.Publisher?.Name ?? GameClientConstants.UnknownVersion,
                    IconUrl = manifest.Metadata?.IconUrl ?? string.Empty,
                    LastUpdated = manifest.Metadata?.ReleaseDate ?? DateTime.Now,
                    DownloadSize = manifest.Files?.Sum(f => f.Size) ?? 0,

                    Data = manifest,
                    RequiresResolution = false,
                    ResolverId = null,
                    SourceUrl = $"filesystem://{manifestEntry.Key}",
                };

                // Copy screenshots and tags
                discovered.ScreenshotUrls.Clear();
                if (manifest.Metadata?.ScreenshotUrls != null && manifest.Metadata.ScreenshotUrls.Count > 0)
                {
                    foreach (var s in manifest.Metadata.ScreenshotUrls)
                    {
                        discovered.ScreenshotUrls.Add(s);
                    }
                }

                discovered.Tags.Clear();
                if (manifest.Metadata?.Tags != null && manifest.Metadata.Tags.Count > 0)
                {
                    foreach (var t in manifest.Metadata.Tags)
                    {
                        discovered.Tags.Add(t);
                    }
                }

                discoveredItems.Add(discovered);
            }
        }

        return OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
        {
            Items = discoveredItems,
            HasMoreItems = false,
        });
    }

    private void InitializeContentDirectories()
    {
        var userDefinedDirs = _configurationProvider.GetContentDirectories();
        _contentDirectories.AddRange(userDefinedDirs.Where(Directory.Exists));

        _logger.LogInformation("FileSystemDiscoverer initialized with {Count} directories", _contentDirectories.Count);
    }
}
