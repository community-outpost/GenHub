using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.ContentResolvers;

/// <summary>
/// Resolves a ContentManifest by reading it directly from a local file path.
/// </summary>
public class LocalManifestResolver(ILogger<LocalManifestResolver> logger) : IContentResolver
{
    private const string FailedToResolveManifestLogMessage = "Failed to resolve manifest from local file: {Path}";

    /// <summary>
    /// Gets the resolver ID for local manifest content.
    /// </summary>
    public string ResolverId => ContentSourceNames.LocalResolverId;

    /// <summary>
    /// Resolves the manifest for discovered local content asynchronously.
    /// </summary>
    /// <param name="discoveredItem">The discovered content.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A OperationResult&lt;ContentManifest&gt; containing the game manifest.</returns>
    public async Task<OperationResult<ContentManifest>> ResolveAsync(ContentSearchResult discoveredItem, CancellationToken cancellationToken = default)
    {
        if (discoveredItem == null)
        {
            return OperationResult<ContentManifest>.CreateFailure("ContentSearchResult cannot be null.");
        }

        if (string.IsNullOrWhiteSpace(discoveredItem.SourceUrl))
        {
            return OperationResult<ContentManifest>.CreateFailure("SourceUrl cannot be null or empty.");
        }

        var manifestPath = discoveredItem.SourceUrl;
        if (!File.Exists(manifestPath))
        {
            return OperationResult<ContentManifest>.CreateFailure($"Manifest file not found at: {manifestPath}");
        }

        try
        {
            var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            var manifest = JsonSerializer.Deserialize<ContentManifest>(manifestJson, ManifestJsonOptions.Default);

            if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id.Value) || manifest.Files == null || manifest.Files.Count == 0)
            {
                return OperationResult<ContentManifest>.CreateFailure("Manifest is missing required fields.");
            }

            return OperationResult<ContentManifest>.CreateSuccess(manifest);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, FailedToResolveManifestLogMessage, manifestPath);
            return OperationResult<ContentManifest>.CreateFailure($"Failed to read or parse local manifest: {ex.Message}");
        }
    }
}
