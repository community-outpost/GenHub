using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;
using GenHub.Features.Tools.Interfaces;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.Services.Hosting;

/// <summary>
/// Hosting provider for Dropbox.
/// Uploads files and catalogs to a designated Dropbox folder and generates direct download links.
/// </summary>
/// <remarks>
/// Recommended for users who:
/// - Already use Dropbox for file storage.
/// - Want simple, reliable hosting with direct download links.
/// - Need more storage than GitHub gists allow.
/// </remarks>
[SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Standard Dropbox API and content endpoints.")]
public class DropboxHostingProvider(ILogger<DropboxHostingProvider> logger, IHttpClientFactory httpClientFactory) : IHostingProvider, IDisposable
{
    private const string DropboxApiUrl = "https://api.dropboxapi.com/2";
    private const string DropboxContentUrl = "https://content.dropboxapi.com/2";
    private const string PublisherFolderPath = HostingConstants.DropboxDefaultPublisherFolder;

    private readonly HttpClient _httpClient = httpClientFactory.CreateClient();
    private string? _accessToken;
    private bool _disposed;

    /// <summary>
    /// Gets the maximum file size supported by Dropbox.
    /// </summary>
    public static long MaxFileSizeBytes => 2L * 1024 * 1024 * 1024; // 2GB free tier limit

    /// <inheritdoc/>
    public string ProviderId => HostingConstants.Dropbox;

    /// <inheritdoc/>
    public string DisplayName => "Dropbox";

    /// <inheritdoc/>
    public string Description => "Host your catalogs and artifacts on Dropbox. 2GB free storage, reliable direct downloads.";

    /// <inheritdoc/>
    public string IconName => "Dropbox";

    /// <inheritdoc/>
    public bool RequiresAuthentication => true;

    /// <inheritdoc/>
    public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken);

    /// <inheritdoc/>
    public bool SupportsCatalogHosting => true;

    /// <inheritdoc/>
    public bool SupportsArtifactHosting => true;

    /// <inheritdoc/>
    public bool SupportsUpdate => true;

    /// <summary>
    /// Authenticates with Dropbox using the stored access token.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success.</returns>
    public Task<OperationResult<bool>> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_accessToken))
        {
            return Task.FromResult(OperationResult<bool>.CreateFailure("Dropbox access token is required. Generate a token from the Dropbox Developer App Console."));
        }

        return AuthenticateWithTokenAsync(_accessToken, cancellationToken);
    }

    /// <summary>
    /// Authenticates with Dropbox using the provided access token and verifies permissions.
    /// </summary>
    /// <param name="accessToken">The Dropbox access token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success.</returns>
    public async Task<OperationResult<bool>> AuthenticateWithTokenAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return OperationResult<bool>.CreateFailure("Dropbox access token cannot be empty.");
        }

        try
        {
            _accessToken = accessToken.Trim();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            // Test connection by getting current account info
            var response = await _httpClient.PostAsync(
                $"{DropboxApiUrl}/users/get_current_account",
                null,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(content);
                var name = doc.RootElement.GetProperty("name").GetProperty("display_name").GetString();
                logger.LogInformation("Successfully authenticated with Dropbox as {UserName}", name);
                return OperationResult<bool>.CreateSuccess(true);
            }

            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("Dropbox authentication failed: {StatusCode} {Error}", response.StatusCode, error);
            _accessToken = null;
            _httpClient.DefaultRequestHeaders.Authorization = null;

            if (error.Contains("missing_scope", StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult<bool>.CreateFailure(
                    "Dropbox token is missing required scopes. Please ensure your Dropbox app has 'account_info.read', 'files.content.write', 'files.content.read', 'sharing.write', and 'sharing.read' enabled under Permissions tab in Dropbox App Console, and regenerate your token.");
            }

            return OperationResult<bool>.CreateFailure($"Dropbox authentication failed: {response.StatusCode}. Please check your access token.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to authenticate with Dropbox");
            _accessToken = null;
            _httpClient.DefaultRequestHeaders.Authorization = null;
            return OperationResult<bool>.CreateFailure($"Dropbox connection error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public Task SignOutAsync()
    {
        _accessToken = null;
        _httpClient.DefaultRequestHeaders.Authorization = null;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<OperationResult<HostingUploadResult>> UploadFileAsync(
        Stream fileStream,
        string fileName,
        string? folderPath = null,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated)
        {
            return OperationResult<HostingUploadResult>.CreateFailure("Not authenticated with Dropbox.");
        }

        try
        {
            folderPath ??= PublisherFolderPath;
            var targetPath = $"{folderPath}/{fileName}".Replace("//", "/");

            logger.LogInformation("Uploading {FileName} to Dropbox at {Path}", fileName, targetPath);
            progress?.Report(10);

            // Upload via Dropbox content API
            var uploadArgs = new
            {
                path = targetPath,
                mode = "overwrite",
                autorename = false,
                mute = false,
                strict_conflict = false,
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{DropboxContentUrl}/files/upload");
            request.Headers.Add("Dropbox-API-Arg", JsonSerializer.Serialize(uploadArgs));
            request.Content = new StreamContent(fileStream, HostingConstants.StreamCopyBufferSize);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(HostingConstants.BinaryContentType);

            progress?.Report(30);

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            progress?.Report(70);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("Dropbox upload failed for {File}: {Status} {Error}", fileName, response.StatusCode, errorContent);
                return OperationResult<HostingUploadResult>.CreateFailure($"Upload failed ({response.StatusCode}): {errorContent}");
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseJson);
            var fileId = doc.RootElement.GetProperty("id").GetString() ?? fileName;
            var fileSize = doc.RootElement.GetProperty("size").GetInt64();

            progress?.Report(80);

            // Create shared link for direct download
            var linkResult = await CreateSharedLinkAsync(targetPath, cancellationToken);
            if (!linkResult.Success)
            {
                return OperationResult<HostingUploadResult>.CreateFailure(linkResult);
            }

            var shareUrl = linkResult.Data;
            var directDownloadUrl = ConvertToDirectDownloadUrl(shareUrl);

            progress?.Report(100);

            return OperationResult<HostingUploadResult>.CreateSuccess(new HostingUploadResult
            {
                FileId = fileId,
                PublicUrl = shareUrl ?? string.Empty,
                DirectDownloadUrl = directDownloadUrl,
                FileSize = fileSize,
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to upload {File} to Dropbox", fileName);
            return OperationResult<HostingUploadResult>.CreateFailure($"Dropbox upload error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<HostingUploadResult>> UploadCatalogAsync(
        string catalogJson,
        string publisherId,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var fileName = $"catalog-{publisherId}.json";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(catalogJson));
        return await UploadFileAsync(stream, fileName, PublisherFolderPath, progress, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<OperationResult<HostingUploadResult>> UpdateFileAsync(
        string fileId,
        Stream fileStream,
        string fileName,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Dropbox overwrites files with mode="overwrite", keeping the same shared link
        return await UploadFileAsync(fileStream, fileName, PublisherFolderPath, progress, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<OperationResult<string>> GetOrCreatePublisherFolderAsync(CancellationToken cancellationToken = default)
    {
        // Dropbox creates folders implicitly on upload
        return Task.FromResult(OperationResult<string>.CreateSuccess(PublisherFolderPath));
    }

    /// <inheritdoc/>
    public async Task<OperationResult<HostingState?>> RecoverHostingStateAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated)
        {
            return OperationResult<HostingState?>.CreateFailure("Not authenticated with Dropbox.");
        }

        try
        {
            logger.LogInformation("Scanning Dropbox folder {Folder} for existing files...", PublisherFolderPath);

            var listArgs = new
            {
                path = PublisherFolderPath,
                recursive = false,
                include_media_info = false,
                include_deleted = false,
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{DropboxApiUrl}/files/list_folder")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(listArgs),
                    Encoding.UTF8,
                    HostingConstants.JsonContentType),
            };

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                if (error.Contains("path/not_found", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogInformation("Dropbox folder {Folder} does not exist yet.", PublisherFolderPath);
                    return OperationResult<HostingState?>.CreateSuccess(new HostingState
                    {
                        ProviderId = ProviderId,
                        FolderId = PublisherFolderPath,
                    });
                }

                return OperationResult<HostingState?>.CreateFailure($"Failed to list Dropbox folder: {error}");
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("entries", out var entries))
            {
                return OperationResult<HostingState?>.CreateFailure("Dropbox list_folder response missing entries property.");
            }

            var state = new HostingState
            {
                ProviderId = ProviderId,
                FolderId = PublisherFolderPath,
                LastPublished = DateTime.UtcNow,
            };

            foreach (var entry in entries.EnumerateArray())
            {
                await ProcessDropboxEntryAsync(entry, state, cancellationToken).ConfigureAwait(false);
            }

            var hasMore = doc.RootElement.TryGetProperty("has_more", out var hasMoreProp) && hasMoreProp.GetBoolean();
            if (hasMore && doc.RootElement.TryGetProperty("cursor", out var cursorProp))
            {
                var cursor = cursorProp.GetString();
                if (!string.IsNullOrEmpty(cursor))
                {
                    await FetchRemainingPagesAsync(cursor, state, cancellationToken).ConfigureAwait(false);
                }
            }

            return OperationResult<HostingState?>.CreateSuccess(state);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to recover Dropbox hosting state");
            return OperationResult<HostingState?>.CreateFailure($"Dropbox scan error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public bool IsValidHostingUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return false;
        }

        return url.Contains("dropbox.com", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("dropboxusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    // skipcq: CS-A1000
    public string GetDirectDownloadUrl(string shareUrl)
    {
        return ConvertToDirectDownloadUrl(shareUrl);
    }

    /// <inheritdoc/>
    public string GetSubscriptionLink(string catalogUrl)
    {
        var encodedUrl = Uri.EscapeDataString(catalogUrl);
        return $"genhub://subscribe?url={encodedUrl}";
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes resources used by the provider.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _httpClient.Dispose();
            }

            _disposed = true;
        }
    }

    private static string ConvertToDirectDownloadUrl(string? shareUrl)
    {
        if (string.IsNullOrEmpty(shareUrl))
        {
            return string.Empty;
        }

        // Convert Dropbox share URL to direct download URL
        // From: https://www.dropbox.com/s/xxxxx/filename?dl=0
        // To: https://dl.dropboxusercontent.com/s/xxxxx/filename
        if (shareUrl.Contains("dropbox.com"))
        {
            return shareUrl
                .Replace("www.dropbox.com", "dl.dropboxusercontent.com")
                .Replace("?dl=0", string.Empty)
                .Replace("?dl=1", string.Empty);
        }

        return shareUrl;
    }

    private static OperationResult<string>? TryExtractMissingScopeError(JsonElement errObj)
    {
        if (errObj.TryGetProperty(".tag", out var tagScope) && tagScope.GetString() == "missing_scope")
        {
            var reqScope = errObj.TryGetProperty("required_scope", out var rs) ? rs.GetString() : "sharing.write";
            return OperationResult<string>.CreateFailure(
                $"Dropbox access token is missing the '{reqScope}' permission. " +
                "In your Dropbox App Console, navigate to the 'Permissions' tab, check 'account_info.read', 'files.content.write', 'files.content.read', 'sharing.write', and 'sharing.read', click 'Submit', and regenerate your token under the 'Settings' tab.");
        }

        return null;
    }

    private async Task FetchRemainingPagesAsync(string initialCursor, HostingState state, CancellationToken cancellationToken)
    {
        var cursor = initialCursor;
        while (!string.IsNullOrEmpty(cursor))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var continueArgs = new { cursor };
            using var continueRequest = new HttpRequestMessage(HttpMethod.Post, $"{DropboxApiUrl}/files/list_folder/continue")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(continueArgs),
                    Encoding.UTF8,
                    HostingConstants.JsonContentType),
            };

            using var continueResponse = await _httpClient.SendAsync(continueRequest, cancellationToken);
            if (!continueResponse.IsSuccessStatusCode)
            {
                var continueError = await continueResponse.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("Dropbox list_folder/continue failed: {Error}", continueError);
                break;
            }

            var continueContent = await continueResponse.Content.ReadAsStringAsync(cancellationToken);
            using var continueDoc = JsonDocument.Parse(continueContent);
            if (continueDoc.RootElement.TryGetProperty("entries", out var continueEntries))
            {
                foreach (var entry in continueEntries.EnumerateArray())
                {
                    await ProcessDropboxEntryAsync(entry, state, cancellationToken).ConfigureAwait(false);
                }
            }

            var hasMore = continueDoc.RootElement.TryGetProperty("has_more", out var nextHasMore) && nextHasMore.GetBoolean();
            cursor = hasMore && continueDoc.RootElement.TryGetProperty("cursor", out var nextCursor) ? nextCursor.GetString() : null;
        }
    }

    private async Task ProcessDropboxEntryAsync(
        JsonElement entry,
        HostingState state,
        CancellationToken cancellationToken)
    {
        if (!entry.TryGetProperty(".tag", out var tagProp) || tagProp.GetString() != "file")
        {
            return;
        }

        var fileName = entry.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
        var fileId = entry.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
        var fileSize = entry.TryGetProperty("size", out var sizeProp) && sizeProp.TryGetInt64(out var size) ? size : 0L;
        var pathLower = entry.TryGetProperty("path_lower", out var pathLowerProp)
            ? pathLowerProp.GetString() ?? $"{PublisherFolderPath}/{fileName}".ToLowerInvariant()
            : $"{PublisherFolderPath}/{fileName}".ToLowerInvariant();

        // Check for existing shared link without creating a new public link for unshared files during scan/recovery
        var linkResult = await TryGetExistingSharedLinkAsync(pathLower, cancellationToken).ConfigureAwait(false);
        var directUrl = (linkResult is { Success: true, Data: not null }) ? ConvertToDirectDownloadUrl(linkResult.Data) : string.Empty;

        if (fileName.Equals("publisher.json", StringComparison.OrdinalIgnoreCase))
        {
            state.Definition = new HostedFileInfo
            {
                FileId = fileId,
                Url = directUrl,
                FileSize = fileSize,
                LastUpdated = DateTime.UtcNow,
            };
            logger.LogInformation("Discovered publisher definition in Dropbox: {Url}", directUrl);
        }
        else if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && fileName.StartsWith("catalog-", StringComparison.OrdinalIgnoreCase))
        {
            var catId = fileName.Replace("catalog-", string.Empty).Replace(".json", string.Empty);
            state.Catalogs.Add(new CatalogHostingInfo
            {
                FileId = fileId,
                CatalogId = catId,
                FileName = fileName,
                CatalogName = catId,
                Url = directUrl,
                FileSize = fileSize,
                LastUpdated = DateTime.UtcNow,
            });
            logger.LogInformation("Discovered catalog '{CatalogId}' in Dropbox: {Url}", catId, directUrl);
        }
        else
        {
            state.Artifacts.Add(new ArtifactHostingInfo
            {
                FileId = fileId,
                FileName = fileName,
                Url = directUrl,
                FileSize = fileSize,
                LastUpdated = DateTime.UtcNow,
            });
            logger.LogInformation("Discovered artifact '{File}' in Dropbox: {Url}", fileName, directUrl);
        }
    }

    private async Task<OperationResult<string>> CreateSharedLinkAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var existingResult = await TryGetExistingSharedLinkAsync(path, cancellationToken).ConfigureAwait(false);
            if (existingResult != null)
            {
                return existingResult;
            }

            return await RequestNewSharedLinkAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception creating shared link for {Path}", path);
            return OperationResult<string>.CreateFailure($"Shared link error: {ex.Message}");
        }
    }

    private async Task<OperationResult<string>?> TryGetExistingSharedLinkAsync(string path, CancellationToken cancellationToken)
    {
        var listRequest = new { path, direct_only = true };
        var listResponse = await _httpClient.PostAsync(
            $"{DropboxApiUrl}/sharing/list_shared_links",
            new StringContent(JsonSerializer.Serialize(listRequest), Encoding.UTF8, HostingConstants.JsonContentType),
            cancellationToken).ConfigureAwait(false);

        if (!listResponse.IsSuccessStatusCode)
        {
            var listError = await listResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            logger.LogWarning("Dropbox list_shared_links returned {Status}: {Error}", listResponse.StatusCode, listError);
            return null;
        }

        var listContent = await listResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var listDoc = JsonDocument.Parse(listContent);
        if (listDoc.RootElement.TryGetProperty("links", out var links) && links.GetArrayLength() > 0)
        {
            var existingUrl = links[0].GetProperty("url").GetString();
            if (!string.IsNullOrEmpty(existingUrl))
            {
                logger.LogInformation("Found existing Dropbox shared link: {Url}", existingUrl);
                return OperationResult<string>.CreateSuccess(existingUrl);
            }
        }

        return null;
    }

    private async Task<OperationResult<string>> RequestNewSharedLinkAsync(string path, CancellationToken cancellationToken)
    {
        var createRequest = new { path };
        var response = await _httpClient.PostAsync(
            $"{DropboxApiUrl}/sharing/create_shared_link_with_settings",
            new StringContent(JsonSerializer.Serialize(createRequest), Encoding.UTF8, HostingConstants.JsonContentType),
            cancellationToken).ConfigureAwait(false);

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            using var resultDoc = JsonDocument.Parse(responseContent);
            var url = resultDoc.RootElement.GetProperty("url").GetString();
            if (!string.IsNullOrEmpty(url))
            {
                logger.LogInformation("Created new Dropbox shared link: {Url}", url);
                return OperationResult<string>.CreateSuccess(url);
            }
        }

        logger.LogWarning("Dropbox create_shared_link_with_settings returned {Status}: {Error}", response.StatusCode, responseContent);
        return ParseSharedLinkErrorResponse(responseContent);
    }

    private OperationResult<string> ParseSharedLinkErrorResponse(string errorContent)
    {
        try
        {
            using var doc = JsonDocument.Parse(errorContent);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var errObj))
            {
                var existingLinkResult = TryExtractExistingSharedLink(errObj);
                if (existingLinkResult != null)
                {
                    return existingLinkResult;
                }

                var missingScopeResult = TryExtractMissingScopeError(errObj);
                if (missingScopeResult != null)
                {
                    return missingScopeResult;
                }
            }
        }
        catch (JsonException)
        {
            // Fall back to returning standard error below
        }

        return OperationResult<string>.CreateFailure($"Failed to create shared link: {errorContent}");
    }

    private OperationResult<string>? TryExtractExistingSharedLink(JsonElement errObj)
    {
        if (errObj.TryGetProperty(".tag", out var tag) &&
            tag.GetString() == "shared_link_already_exists" &&
            errObj.TryGetProperty("metadata", out var meta) &&
            meta.TryGetProperty("url", out var existingUrlProp))
        {
            var recoveredUrl = existingUrlProp.GetString();
            if (!string.IsNullOrEmpty(recoveredUrl))
            {
                logger.LogInformation("Extracted existing link from shared_link_already_exists error: {Url}", recoveredUrl);
                return OperationResult<string>.CreateSuccess(recoveredUrl);
            }
        }

        return null;
    }
}
