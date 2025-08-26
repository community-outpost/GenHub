using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.ContentResolvers;

/// <summary>
/// Resolves a ContentManifest by reading it directly from a local file path.
/// </summary>
public class LocalManifestResolver(ILogger<LocalManifestResolver> logger) : IContentResolver
{
    /// <summary>
    /// Gets the resolver ID for local manifest content.
    /// </summary>
    public string ResolverId => "LocalManifest";

    /// <summary>
    /// Resolves the manifest for discovered local content asynchronously.
    /// </summary>
    /// <param name="discoveredItem">The discovered content.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A ContentOperationResult&lt;ContentManifest&gt; containing the game manifest.</returns>
    public async Task<ContentOperationResult<ContentManifest>> ResolveAsync(ContentSearchResult discoveredItem, CancellationToken cancellationToken = default)
    {
        if (discoveredItem == null)
        {
            return ContentOperationResult<ContentManifest>.CreateFailure("ContentSearchResult cannot be null.");
        }

        if (string.IsNullOrWhiteSpace(discoveredItem.SourceUrl))
        {
            return ContentOperationResult<ContentManifest>.CreateFailure("SourceUrl cannot be null or empty.");
        }

        var manifestPath = discoveredItem.SourceUrl;
        if (!File.Exists(manifestPath))
        {
            return ContentOperationResult<ContentManifest>.CreateFailure($"Manifest file not found at: {manifestPath}");
        }

        try
        {
            var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            var manifest = JsonSerializer.Deserialize<ContentManifest>(manifestJson);

            if (manifest == null)
            {
                return ContentOperationResult<ContentManifest>.CreateFailure("Failed to deserialize manifest.");
            }

            return ContentOperationResult<ContentManifest>.CreateSuccess(manifest);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve manifest from local file: {Path}", manifestPath);
            return ContentOperationResult<ContentManifest>.CreateFailure($"Failed to read or parse local manifest: {ex.Message}");
        }
    }
}
