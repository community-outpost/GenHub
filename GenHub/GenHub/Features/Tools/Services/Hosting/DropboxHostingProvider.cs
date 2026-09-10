using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
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
/// Enables publishers to host catalogs and artifacts on Dropbox.
/// </summary>
/// <remarks>
/// Dropbox is a good option for publishers who:
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

    /// <inheritdoc/>
    public string ProviderId => HostingConstants.Dropbox;

    /// <inheritdoc/>
    public string DisplayName => "Dropbox";

    /// <inheritdoc/>
    public string Description => "Host your catalogs and artifacts on Dropbox. Simple and reliable with direct download links.";

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
    /// Gets the maximum file size supported by Dropbox.
    /// </summary>
    public long MaxFileSizeBytes => 2L * 1024 * 1024 * 1024; // 2GB free tier limit

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
    /// Authenticates with Dropbox using a personal access token.
    /// </summary>
    /// <param name="accessToken">The Dropbox access token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success.</returns>
    public async Task<OperationResult<bool>> AuthenticateWithTokenAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return OperationResult<bool>.CreateFailure("Access token is required");
        }

        try
        {
            // Verify the token by getting current account info without modifying client default headers
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{DropboxApiUrl}/users/get_current_account")
            {
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", accessToken) },
                Content = new StringContent("null", Encoding.UTF8, HostingConstants.JsonContentType),
            };

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _accessToken = accessToken;
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                logger.LogInformation("Successfully authenticated with Dropbox");
                return OperationResult<bool>.CreateSuccess(true);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("Dropbox authentication failed (HTTP {StatusCode}): {Error}", response.StatusCode, errorContent);
                return OperationResult<bool>.CreateFailure(
                    $"Dropbox authentication failed (HTTP {(int)response.StatusCode}). Verify your access token.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Dropbox authentication failed");
            return OperationResult<bool>.CreateFailure($"Authentication failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public Task SignOutAsync()
    {
        _accessToken = null;
        _httpClient.DefaultRequestHeaders.Authorization = null;
        logger.LogInformation("Signed out from Dropbox");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<OperationResult<string>> GetOrCreatePublisherFolderAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated)
        {
            return OperationResult<string>.CreateFailure("Not authenticated with Dropbox");
        }

        try
        {
            // Check if folder exists
            var checkFolderRequest = new
            {
                path = PublisherFolderPath,
                include_media_info = false,
                include_deleted = false,
                include_has_explicit_shared_members = false,
            };

            var checkResponse = await _httpClient.PostAsync(
                $"{DropboxApiUrl}/files/get_metadata",
                new StringContent(JsonSerializer.Serialize(checkFolderRequest), Encoding.UTF8, HostingConstants.JsonContentType),
                cancellationToken);

            if (checkResponse.IsSuccessStatusCode)
            {
                return OperationResult<string>.CreateSuccess(PublisherFolderPath);
            }

            // Create the folder
            var createFolderRequest = new
            {
                path = PublisherFolderPath,
                autorename = false,
            };

            var createResponse = await _httpClient.PostAsync(
                $"{DropboxApiUrl}/files/create_folder_v2",
                new StringContent(JsonSerializer.Serialize(createFolderRequest), Encoding.UTF8, HostingConstants.JsonContentType),
                cancellationToken);

            if (createResponse.IsSuccessStatusCode)
            {
                logger.LogInformation("Created publisher folder: {Path}", PublisherFolderPath);
                return OperationResult<string>.CreateSuccess(PublisherFolderPath);
            }

            if (createResponse.StatusCode == HttpStatusCode.Conflict)
            {
                logger.LogInformation("Publisher folder already exists (conflict): {Path}", PublisherFolderPath);
                return OperationResult<string>.CreateSuccess(PublisherFolderPath);
            }

            var error = await createResponse.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("Failed to create folder: {Error}", error);
            return OperationResult<string>.CreateFailure($"Failed to create folder: {error}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting/creating Dropbox folder");
            return OperationResult<string>.CreateFailure($"Folder operation failed: {ex.Message}");
        }
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
            return OperationResult<HostingUploadResult>.CreateFailure("Not authenticated with Dropbox");
        }

        try
        {
            var targetFolder = folderPath ?? PublisherFolderPath;
            var filePath = $"{targetFolder}/{fileName}".Replace("//", "/");

            logger.LogInformation("Uploading file to Dropbox: {Path}", filePath);

            progress?.Report(10);

            // Read stream into byte array for upload
            using var memoryStream = new MemoryStream();
            await fileStream.CopyToAsync(memoryStream, cancellationToken);
            var fileBytes = memoryStream.ToArray();

            progress?.Report(30);

            // Dropbox upload uses content-upload endpoint
            var uploadArgs = new
            {
                path = filePath,
                mode = "overwrite",
                autorename = false,
                mute = false,
                strict_conflict = false,
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{DropboxContentUrl}/files/upload");
            request.Headers.Add("Dropbox-API-Arg", JsonSerializer.Serialize(uploadArgs));
            request.Content = new ByteArrayContent(fileBytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(HostingConstants.BinaryContentType);

            var response = await _httpClient.SendAsync(request, cancellationToken);

            progress?.Report(70);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("Dropbox files/upload failed ({StatusCode}): {Error}", response.StatusCode, error);
                return OperationResult<HostingUploadResult>.CreateFailure($"Upload failed: {error}");
            }

            // Create or fetch shared link
            var shareResult = await CreateSharedLinkAsync(filePath, cancellationToken);
            if (!shareResult.Success || string.IsNullOrEmpty(shareResult.Data))
            {
                logger.LogError("Failed to get shared link for {Path}: {Error}", filePath, shareResult.FirstError);
                return OperationResult<HostingUploadResult>.CreateFailure(shareResult);
            }

            progress?.Report(100);

            var result = new HostingUploadResult
            {
                PublicUrl = shareResult.Data,
                DirectDownloadUrl = ConvertToDirectDownloadUrl(shareResult.Data),
                FileId = filePath,
                FileSize = fileBytes.Length,
            };

            logger.LogInformation("Uploaded file to Dropbox: {Path} ({Size} bytes) -> {Url}", filePath, fileBytes.Length, result.DirectDownloadUrl);
            return OperationResult<HostingUploadResult>.CreateSuccess(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to upload file to Dropbox");
            return OperationResult<HostingUploadResult>.CreateFailure($"Upload failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<HostingUploadResult>> UpdateFileAsync(
        string fileId,
        Stream fileStream,
        string fileName,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // For Dropbox, updating is the same as uploading with overwrite mode
        return await UploadFileAsync(fileStream, fileName, null, progress, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<OperationResult<HostingUploadResult>> UploadCatalogAsync(
        string catalogJson,
        string publisherId,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var bytes = Encoding.UTF8.GetBytes(catalogJson);
        using var stream = new MemoryStream(bytes);
        var fileName = $"catalog-{publisherId}.json";
        return await UploadFileAsync(stream, fileName, null, progress, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<OperationResult<HostingState?>> RecoverHostingStateAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated)
        {
            return OperationResult<HostingState?>.CreateFailure("Not authenticated with Dropbox");
        }

        try
        {
            logger.LogInformation("Scanning Dropbox folder {Folder} for hosted files...", PublisherFolderPath);
            var listFolderRequest = new { path = PublisherFolderPath, recursive = false };
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{DropboxApiUrl}/files/list_folder")
            {
                Content = new StringContent(JsonSerializer.Serialize(listFolderRequest), Encoding.UTF8, HostingConstants.JsonContentType),
            };

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("Dropbox list_folder failed ({StatusCode}): {Error}", response.StatusCode, errorContent);
                return OperationResult<HostingState?>.CreateSuccess(null);
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            {
                return OperationResult<HostingState?>.CreateSuccess(null);
            }

            var state = new HostingState
            {
                ProviderId = HostingConstants.Dropbox,
                FolderUrl = $"https://www.dropbox.com/home{PublisherFolderPath}",
                LastPublished = DateTime.UtcNow,
            };

            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.TryGetProperty(".tag", out var tag) && tag.GetString() != "file")
                {
                    continue;
                }

                var name = entry.GetProperty("name").GetString() ?? string.Empty;
                var pathDisplay = entry.GetProperty("path_display").GetString() ?? $"{PublisherFolderPath}/{name}";
                var id = entry.GetProperty("id").GetString() ?? pathDisplay;
                var size = entry.TryGetProperty("size", out var sizeProp) ? sizeProp.GetInt64() : 0L;
                var lastModified = entry.TryGetProperty("server_modified", out var modProp) && modProp.TryGetDateTime(out var dt)
                    ? dt
                    : DateTime.UtcNow;

                var shareResult = await CreateSharedLinkAsync(pathDisplay, cancellationToken);
                var directUrl = shareResult.Success && !string.IsNullOrEmpty(shareResult.Data)
                    ? ConvertToDirectDownloadUrl(shareResult.Data)
                    : string.Empty;

                if (name.Equals(HostingConstants.DefaultDefinitionFileName, StringComparison.OrdinalIgnoreCase))
                {
                    state.Definition = new HostedFileInfo
                    {
                        FileId = id,
                        Url = directUrl,
                        FileSize = size,
                        LastUpdated = lastModified,
                    };
                }
                else if (name.StartsWith("catalog-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    var catalogId = name["catalog-".Length..^".json".Length];
                    state.Catalogs.Add(new CatalogHostingInfo
                    {
                        CatalogId = catalogId,
                        CatalogName = catalogId,
                        FileName = name,
                        FileId = id,
                        Url = directUrl,
                        FileSize = size,
                        LastUpdated = lastModified,
                    });
                }
                else
                {
                    state.Artifacts.Add(new ArtifactHostingInfo
                    {
                        FileName = name,
                        FileId = id,
                        Url = directUrl,
                        FileSize = size,
                        LastUpdated = lastModified,
                    });
                }
            }

            logger.LogInformation(
                "Dropbox scan completed. Found {Catalogs} catalogs, {Artifacts} artifacts, Definition={HasDef}",
                state.Catalogs.Count,
                state.Artifacts.Count,
                state.Definition != null);
            return OperationResult<HostingState?>.CreateSuccess(state);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to scan Dropbox folder for hosted state");
            return OperationResult<HostingState?>.CreateFailure($"Failed to scan Dropbox: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public string GetSubscriptionLink(string catalogUrl)
    {
        return $"genhub://subscribe?url={Uri.EscapeDataString(catalogUrl)}";
    }

    /// <inheritdoc/>
    public bool IsValidHostingUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return false;
        }

        return url.Contains("dropbox.com", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("dl.dropboxusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public string GetDirectDownloadUrl(string shareUrl)
    {
        return ConvertToDirectDownloadUrl(shareUrl);
    }

    /// <summary>
    /// Disposes resources used by the provider.
    /// </summary>
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

    private static string ConvertToDirectDownloadUrl(string shareUrl)
    {
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

    private async Task<OperationResult<string>> CreateSharedLinkAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            // 1. Check existing shared link
            var listRequest = new { path, direct_only = true };
            var listResponse = await _httpClient.PostAsync(
                $"{DropboxApiUrl}/sharing/list_shared_links",
                new StringContent(JsonSerializer.Serialize(listRequest), Encoding.UTF8, HostingConstants.JsonContentType),
                cancellationToken);

            if (listResponse.IsSuccessStatusCode)
            {
                var listContent = await listResponse.Content.ReadAsStringAsync(cancellationToken);
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
            }
            else
            {
                var listError = await listResponse.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("Dropbox list_shared_links returned {Status}: {Error}", listResponse.StatusCode, listError);
            }

            // 2. Create new shared link sending minimal { path }
            var createRequest = new { path };
            var response = await _httpClient.PostAsync(
                $"{DropboxApiUrl}/sharing/create_shared_link_with_settings",
                new StringContent(JsonSerializer.Serialize(createRequest), Encoding.UTF8, HostingConstants.JsonContentType),
                cancellationToken);

            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                using var resultDoc = JsonDocument.Parse(errorContent);
                var url = resultDoc.RootElement.GetProperty("url").GetString();
                if (!string.IsNullOrEmpty(url))
                {
                    logger.LogInformation("Created new Dropbox shared link: {Url}", url);
                    return OperationResult<string>.CreateSuccess(url);
                }
            }

            logger.LogWarning("Dropbox create_shared_link_with_settings returned {Status}: {Error}", response.StatusCode, errorContent);

            // 3. Inspect error for recovery (e.g. shared_link_already_exists or missing_scope)
            try
            {
                using var doc = JsonDocument.Parse(errorContent);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var errObj))
                {
                    // Check if link already exists
                    if (errObj.TryGetProperty(".tag", out var tag) && tag.GetString() == "shared_link_already_exists")
                    {
                        if (errObj.TryGetProperty("metadata", out var meta) && meta.TryGetProperty("url", out var existingUrlProp))
                        {
                            var recoveredUrl = existingUrlProp.GetString();
                            if (!string.IsNullOrEmpty(recoveredUrl))
                            {
                                logger.LogInformation("Extracted existing link from shared_link_already_exists error: {Url}", recoveredUrl);
                                return OperationResult<string>.CreateSuccess(recoveredUrl);
                            }
                        }
                    }

                    // Check for missing permissions
                    if (errObj.TryGetProperty(".tag", out var tagScope) && tagScope.GetString() == "missing_scope")
                    {
                        var reqScope = errObj.TryGetProperty("required_scope", out var rs) ? rs.GetString() : "sharing.write";
                        return OperationResult<string>.CreateFailure(
                            $"Dropbox access token is missing the '{reqScope}' permission. " +
                            "In your Dropbox App Console, navigate to the 'Permissions' tab, check 'account_info.read', 'files.content.write', 'files.content.read', 'sharing.write', and 'sharing.read', click 'Submit', and regenerate your token under the 'Settings' tab.");
                    }
                }
            }
            catch (JsonException)
            {
                // Fall back to returning standard error below
            }

            return OperationResult<string>.CreateFailure($"Failed to create shared link: {errorContent}");
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
}
