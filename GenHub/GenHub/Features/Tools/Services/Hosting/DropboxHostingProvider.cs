using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;
using GenHub.Features.Tools.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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
public class DropboxHostingProvider(ILogger<DropboxHostingProvider> logger, IHttpClientFactory httpClientFactory, ILocalizationService? localizationService = null) : IHostingProvider, IDisposable
{
    private const string DropboxApiUrl = HostingConstants.DropboxApiUrl;
    private const string DropboxContentUrl = HostingConstants.DropboxContentUrl;
    private const string PublisherFolderPath = HostingConstants.DropboxDefaultPublisherFolder;
    private const string MissingScopeTag = "missing_scope";

    private readonly HttpClient _httpClient = InitializeHttpClient(httpClientFactory);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private string? _accessToken;
    private string? _appKey;
    private string? _refreshToken;
    private DateTime _accessTokenExpiresAtUtc = DateTime.MinValue;
    private DropboxOAuthService? _oauthService;
    private bool _disposed;

    /// <summary>
    /// Gets the maximum file size supported by the Dropbox simple upload endpoint (150 MB).
    /// </summary>
    public static long MaxFileSizeBytes => 150L * 1024 * 1024; // 150MB simple upload endpoint limit

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
    /// OAuth credentials refresh the token first when it is expired or close to expiry.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success.</returns>
    public async Task<OperationResult<bool>> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        if (HasOAuthCredentials)
        {
            await EnsureFreshAccessTokenAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(_accessToken))
        {
            return OperationResult<bool>.CreateFailure("Dropbox access token is required. Generate a token from the Dropbox Developer App Console.");
        }

        return await AuthenticateWithTokenAsync(_accessToken, cancellationToken);
    }

    /// <summary>
    /// Gets a value indicating whether OAuth credentials (app key plus refresh token) are stored.
    /// </summary>
    public bool HasOAuthCredentials => !string.IsNullOrWhiteSpace(_appKey) && !string.IsNullOrWhiteSpace(_refreshToken);

    /// <summary>
    /// Restores previously stored OAuth credentials without contacting Dropbox.
    /// The next operation refreshes the access token when it is expired or close to expiry.
    /// </summary>
    /// <param name="credential">The stored OAuth credential.</param>
    public void SetOAuthCredentials(DropboxOAuthCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        _appKey = credential.AppKey;
        _accessToken = credential.AccessToken;
        _refreshToken = credential.RefreshToken;
        _accessTokenExpiresAtUtc = credential.ExpiresAtUtc;
    }

    /// <summary>
    /// Exports the current OAuth credential set for the secure credential store.
    /// </summary>
    /// <returns>The serialized payload, or null when no OAuth credentials exist.</returns>
    public string? ExportCredentialPayload()
    {
        if (string.IsNullOrWhiteSpace(_appKey) || string.IsNullOrWhiteSpace(_accessToken))
        {
            return null;
        }

        return DropboxOAuthService.SerializeCredential(new DropboxOAuthCredential(
            _appKey,
            _accessToken,
            _refreshToken,
            _accessTokenExpiresAtUtc));
    }

    /// <summary>
    /// Authenticates with Dropbox using the interactive OAuth 2.0 authorization-code flow with PKCE.
    /// Opens the system browser; the resulting refresh token keeps the session alive without
    /// pasting new tokens every few hours.
    /// </summary>
    /// <param name="appKey">The Dropbox application key from the user's own Dropbox app.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success.</returns>
    public async Task<OperationResult<bool>> AuthenticateWithOAuthAsync(string appKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appKey))
        {
            return OperationResult<bool>.CreateFailure("A Dropbox app key is required. Create an app in the Dropbox Developer App Console and paste its app key.");
        }

        var service = GetOAuthService();
        var (verifier, challenge) = DropboxOAuthService.CreatePkcePair();
        var state = DropboxOAuthService.CreateOAuthState();
        var listenerResult = StartOAuthListener();
        if (!listenerResult.Success)
        {
            return OperationResult<bool>.CreateFailure(listenerResult);
        }

        using var listener = listenerResult.Data.Listener;
        var redirectUri = listenerResult.Data.RedirectUri;

        var authorizeUrl = DropboxOAuthService.BuildAuthorizeUrl(appKey.Trim(), challenge, redirectUri, state);
        if (!service.TryOpenBrowser(authorizeUrl))
        {
            return OperationResult<bool>.CreateFailure($"Could not open the browser. Please visit this URL to connect Dropbox: {authorizeUrl}");
        }

        var codeResult = await service.WaitForAuthorizationCodeAsync(listener, state, cancellationToken);
        if (!codeResult.Success || string.IsNullOrEmpty(codeResult.Data))
        {
            return OperationResult<bool>.CreateFailure(codeResult);
        }

        var tokenResult = await service.ExchangeCodeForTokensAsync(appKey.Trim(), codeResult.Data, verifier, redirectUri, cancellationToken);
        if (!tokenResult.Success || tokenResult.Data == null)
        {
            return OperationResult<bool>.CreateFailure(tokenResult);
        }

        return await ApplyOAuthTokensAsync(appKey.Trim(), tokenResult.Data, cancellationToken);
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

            // Test connection by getting current account info
            using var accountRequest = CreateAuthorizedRequest(HttpMethod.Post, $"{DropboxApiUrl}/users/get_current_account", _accessToken);
            var response = await _httpClient.SendAsync(accountRequest, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(content);
                var name = doc.RootElement.GetProperty("name").GetProperty("display_name").GetString();

                // Test sharing permissions (required for publishing and sharing links)
                var sharingCheckBody = new { direct_only = true };
                using var sharingRequest = CreateAuthorizedRequest(HttpMethod.Post, $"{DropboxApiUrl}/sharing/list_shared_links", _accessToken);
                sharingRequest.Content = new StringContent(JsonSerializer.Serialize(sharingCheckBody), Encoding.UTF8, HostingConstants.JsonContentType);

                using var sharingResponse = await _httpClient.SendAsync(sharingRequest, cancellationToken).ConfigureAwait(false);
                if (!sharingResponse.IsSuccessStatusCode)
                {
                    var sharingError = await sharingResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    logger.LogWarning("Dropbox permissions check failed: {StatusCode} {Error}", sharingResponse.StatusCode, sharingError);

                    _accessToken = null;

                    if (sharingError.Contains("sharing.read", StringComparison.OrdinalIgnoreCase) ||
                        sharingError.Contains(MissingScopeTag, StringComparison.OrdinalIgnoreCase) ||
                        sharingError.Contains("not permitted", StringComparison.OrdinalIgnoreCase))
                    {
                        return OperationResult<bool>.CreateFailure(
                            "Dropbox app is missing the required 'sharing.read' scope. " +
                            "In your Dropbox Developer App Console, go to your app -> 'Permissions' tab, enable 'account_info.read', 'files.content.write', 'files.content.read', 'sharing.read', and 'sharing.write', click 'Submit' at the bottom, and then generate a new token under 'Settings'.");
                    }

                    return OperationResult<bool>.CreateFailure($"Dropbox permissions check failed: {sharingResponse.StatusCode}. Ensure your app has all required permissions.");
                }

                logger.LogInformation("Successfully authenticated with Dropbox as {UserName}", name);
                return OperationResult<bool>.CreateSuccess(true);
            }

            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("Dropbox authentication failed: {StatusCode} {Error}", response.StatusCode, error);
            _accessToken = null;

            if (error.Contains(MissingScopeTag, StringComparison.OrdinalIgnoreCase))
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
            return OperationResult<bool>.CreateFailure($"Dropbox connection error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public Task SignOutAsync()
    {
        _accessToken = null;
        _appKey = null;
        _refreshToken = null;
        _accessTokenExpiresAtUtc = DateTime.MinValue;
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
            await EnsureFreshAccessTokenAsync(cancellationToken);

            folderPath ??= PublisherFolderPath;
            var safeFileName = SanitizeFileName(fileName);
            if (safeFileName == null)
            {
                return OperationResult<HostingUploadResult>.CreateFailure($"Invalid file name '{fileName}'. File names must not contain path separators.");
            }

            var targetPath = $"{folderPath}/{safeFileName}".Replace("//", "/");

            logger.LogInformation("Uploading {FileName} to Dropbox at {Path}", safeFileName, targetPath);
            progress?.Report(10);

            // Buffer the payload up front so the expired-token retry below can re-send
            // the exact same bytes. The caller's stream may be non-seekable or no longer
            // readable once the first attempt completes, which would make the retry dead.
            var payloadResult = await BufferUploadPayloadAsync(fileStream, safeFileName, cancellationToken);
            if (!payloadResult.Success || payloadResult.Data == null)
            {
                return OperationResult<HostingUploadResult>.CreateFailure(payloadResult);
            }

            var payload = payloadResult.Data;

            // Upload via Dropbox content API
            var uploadArgs = new
            {
                path = targetPath,
                mode = "overwrite",
                autorename = false,
                mute = false,
                strict_conflict = false,
            };

            using var request = CreateUploadRequest(uploadArgs, payload);

            progress?.Report(30);

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            progress?.Report(70);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                if (DropboxOAuthService.IsExpiredTokenError(response.StatusCode, errorContent))
                {
                    (response, errorContent) = await TryRetryUploadAfterRefreshAsync(
                        response,
                        errorContent,
                        uploadArgs,
                        payload,
                        safeFileName,
                        cancellationToken);
                }

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogError("Dropbox upload failed for {File}: {Status} {Error}", safeFileName, response.StatusCode, errorContent);
                    return OperationResult<HostingUploadResult>.CreateFailure($"Upload failed ({response.StatusCode}): {errorContent}");
                }
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseJson);
            var fileId = doc.RootElement.GetProperty("id").GetString() ?? safeFileName;
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
        string? catalogFileName = null,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var fileName = string.IsNullOrWhiteSpace(catalogFileName) ? $"catalog-{publisherId}.json" : catalogFileName;
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
    public async Task<OperationResult<bool>> DeleteFileAsync(string fileId, CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated)
        {
            return OperationResult<bool>.CreateFailure("Not authenticated with Dropbox.");
        }

        if (string.IsNullOrWhiteSpace(fileId))
        {
            return OperationResult<bool>.CreateFailure("A Dropbox file ID or path is required to delete a file.");
        }

        try
        {
            await EnsureFreshAccessTokenAsync(cancellationToken);

            // Dropbox file IDs (id:...) are accepted anywhere a path is expected
            var deleteArgs = new { path = fileId.Trim() };
            using var request = CreateAuthorizedRequest(HttpMethod.Post, $"{DropboxApiUrl}/files/delete_v2", _accessToken);
            request.Content = new StringContent(
                JsonSerializer.Serialize(deleteArgs),
                Encoding.UTF8,
                HostingConstants.JsonContentType);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Deleted Dropbox file {FileId}", fileId);
                return OperationResult<bool>.CreateSuccess(true);
            }

            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (error.Contains("path_lookup/not_found", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("path/not_found", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("Dropbox file {FileId} was already deleted.", fileId);
                return OperationResult<bool>.CreateSuccess(true);
            }

            logger.LogWarning("Dropbox delete failed for {FileId}: {Status} {Error}", fileId, response.StatusCode, error);
            return OperationResult<bool>.CreateFailure($"Dropbox delete failed ({response.StatusCode}): {error}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete Dropbox file {FileId}", fileId);
            return OperationResult<bool>.CreateFailure($"Dropbox delete error: {ex.Message}");
        }
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
            await EnsureFreshAccessTokenAsync(cancellationToken);

            logger.LogInformation("Scanning Dropbox folder {Folder} for existing files...", PublisherFolderPath);

            var listArgs = new
            {
                path = PublisherFolderPath,
                recursive = false,
                include_media_info = false,
                include_deleted = false,
            };

            using var request = CreateAuthorizedRequest(HttpMethod.Post, $"{DropboxApiUrl}/files/list_folder", _accessToken);
            request.Content = new StringContent(
                JsonSerializer.Serialize(listArgs),
                Encoding.UTF8,
                HostingConstants.JsonContentType);

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

            // LastPublished is intentionally left unset: recovery rediscovers existing
            // state rather than publishing, so there is no meaningful publish time.
            var state = new HostingState
            {
                ProviderId = ProviderId,
                FolderId = PublisherFolderPath,
            };

            foreach (var entry in entries.EnumerateArray())
            {
                var entryResult = await ProcessDropboxEntryAsync(entry, state, cancellationToken).ConfigureAwait(false);
                if (!entryResult.Success)
                {
                    logger.LogWarning("Skipping Dropbox entry during state recovery: {Error}", entryResult.FirstError);
                }
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

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return HostingConstants.IsCloudProviderHost(uri.Host) &&
            (IsDropboxHost(uri.Host, "dropbox.com") || IsDropboxHost(uri.Host, "dropboxusercontent.com"));
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
        return CommandLineConstants.BuildSubscriptionUrl(catalogUrl);
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
            // Note: _httpClient was created by IHttpClientFactory, which manages the lifecycle
            // and handler pooling. Disposing the factory-managed client is skipped to prevent
            // ObjectDisposedException on shared handlers.
            _refreshLock.Dispose();
            _disposed = true;
        }
    }

    private static HttpClient InitializeHttpClient(IHttpClientFactory factory)
    {
        var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(5);
        return client;
    }

    private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string requestUri, string? accessToken)
    {
        var request = new HttpRequestMessage(method, requestUri);
        if (!string.IsNullOrEmpty(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return request;
    }

    private static string? SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var leaf = fileName.Replace('\\', '/');
        var separatorIndex = leaf.LastIndexOf('/');
        if (separatorIndex >= 0)
        {
            leaf = leaf[(separatorIndex + 1)..];
        }

        if (string.IsNullOrWhiteSpace(leaf) || leaf == "." || leaf == "..")
        {
            return null;
        }

        return leaf;
    }

    private static bool IsDropboxHost(string host, string domain)
    {
        return host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime GetEntryTimestamp(JsonElement entry)
    {
        if (entry.TryGetProperty("server_modified", out var serverModified) &&
            TryParseDropboxTimestamp(serverModified.GetString(), out var serverTime))
        {
            return serverTime;
        }

        if (entry.TryGetProperty("client_modified", out var clientModified) &&
            TryParseDropboxTimestamp(clientModified.GetString(), out var clientTime))
        {
            return clientTime;
        }

        return DateTime.UtcNow;
    }

    private static bool TryParseDropboxTimestamp(string? value, out DateTime result)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            result = parsed.UtcDateTime;
            return true;
        }

        result = DateTime.UtcNow;
        return false;
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
        if (shareUrl.Contains("dropbox.com", StringComparison.OrdinalIgnoreCase))
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
        if (errObj.TryGetProperty(".tag", out var tagScope) && tagScope.GetString() == MissingScopeTag)
        {
            var reqScope = errObj.TryGetProperty("required_scope", out var rs) ? rs.GetString() : "sharing.write";
            return OperationResult<string>.CreateFailure(
                $"Dropbox access token is missing the '{reqScope}' permission. " +
                "In your Dropbox App Console, navigate to the 'Permissions' tab, check 'account_info.read', 'files.content.write', 'files.content.read', 'sharing.write', and 'sharing.read', click 'Submit', and regenerate your token under the 'Settings' tab.");
        }

        return null;
    }

    private static OperationResult<string>? HandleListSharedLinksFailure(string listError)
    {
        if (string.IsNullOrEmpty(listError))
        {
            return null;
        }

        try
        {
            using var errDoc = JsonDocument.Parse(listError);
            if (errDoc.RootElement.TryGetProperty("error", out var errObj))
            {
                var scopeErr = TryExtractMissingScopeError(errObj);
                if (scopeErr != null)
                {
                    return scopeErr;
                }
            }
        }
        catch (JsonException)
        {
            // Fallback to substring matching
        }

        if (listError.Contains("sharing.read", StringComparison.OrdinalIgnoreCase) ||
            listError.Contains(MissingScopeTag, StringComparison.OrdinalIgnoreCase) ||
            listError.Contains("not permitted", StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<string>.CreateFailure(
                "Dropbox access token is missing the 'sharing.read' permission. In your Dropbox App Console, navigate to the 'Permissions' tab, check 'account_info.read', 'files.content.write', 'files.content.read', 'sharing.write', and 'sharing.read', click 'Submit', and regenerate your token under the 'Settings' tab.");
        }

        return null;
    }

    private static async Task<OperationResult<byte[]>> BufferUploadPayloadAsync(Stream fileStream, string safeFileName, CancellationToken cancellationToken)
    {
        if (fileStream.CanSeek && fileStream.Length > MaxFileSizeBytes)
        {
            return OperationResult<byte[]>.CreateFailure(
                $"File '{safeFileName}' is {fileStream.Length / (1024 * 1024)} MB, which exceeds Dropbox's 150 MB single-request upload limit.");
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[HostingConstants.StreamCopyBufferSize];
        int read;
        while ((read = await fileStream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxFileSizeBytes)
            {
                return OperationResult<byte[]>.CreateFailure(
                    $"File '{safeFileName}' exceeds Dropbox's 150 MB single-request upload limit.");
            }

            buffer.Write(chunk, 0, read);
        }

        return OperationResult<byte[]>.CreateSuccess(buffer.ToArray());
    }

    private DropboxOAuthService GetOAuthService()
    {
        _oauthService ??= new DropboxOAuthService(_httpClient, logger, localizationService);
        return _oauthService;
    }

    private OperationResult<(System.Net.HttpListener Listener, string RedirectUri)> StartOAuthListener()
    {
        try
        {
            var started = DropboxOAuthService.StartLoopbackListener();
            return OperationResult<(System.Net.HttpListener Listener, string RedirectUri)>.CreateSuccess((started.Listener, started.RedirectUri));
        }
        catch (Exception ex) when (ex is System.Net.HttpListenerException or System.Net.Sockets.SocketException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Dropbox OAuth loopback listener failed to start");
            return OperationResult<(System.Net.HttpListener Listener, string RedirectUri)>.CreateFailure(
                ex is InvalidOperationException ? ex.Message : "Could not listen for the Dropbox sign-in response on this machine.");
        }
    }

    private async Task<OperationResult<bool>> ApplyOAuthTokensAsync(
        string appKey,
        DropboxTokenResult tokens,
        CancellationToken cancellationToken)
    {
        var verifyResult = await AuthenticateWithTokenAsync(tokens.AccessToken, cancellationToken);
        if (!verifyResult.Success)
        {
            return verifyResult;
        }

        _appKey = appKey;
        if (!string.IsNullOrWhiteSpace(tokens.RefreshToken))
        {
            _refreshToken = tokens.RefreshToken;
        }

        _accessTokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(tokens.ExpiresInSeconds, 0));
        return verifyResult;
    }

    private async Task EnsureFreshAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!HasOAuthCredentials)
        {
            return;
        }

        var refreshAt = _accessTokenExpiresAtUtc.AddMinutes(-HostingConstants.OAuthRefreshBeforeExpiryMinutes);
        if (DateTime.UtcNow < refreshAt && !string.IsNullOrWhiteSpace(_accessToken))
        {
            return;
        }

        await TryRefreshAccessTokenAsync(cancellationToken);
    }

    private async Task<bool> TryRefreshAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_appKey) || string.IsNullOrWhiteSpace(_refreshToken))
        {
            return false;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            var result = await GetOAuthService().RefreshAccessTokenAsync(_appKey, _refreshToken, cancellationToken);
            if (!result.Success || result.Data == null)
            {
                logger.LogWarning("Dropbox token refresh failed: {Error}", result.FirstError);
                if (result.FirstError?.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase) == true)
                {
                    _refreshToken = null;
                    _accessToken = null;
                }

                return false;
            }

            _accessToken = result.Data.AccessToken;
            if (!string.IsNullOrWhiteSpace(result.Data.RefreshToken))
            {
                _refreshToken = result.Data.RefreshToken;
            }

            _accessTokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(result.Data.ExpiresInSeconds, 0));
            logger.LogInformation("Refreshed Dropbox access token");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Dropbox token refresh request failed");
            return false;
        }
        catch (ObjectDisposedException ex)
        {
            logger.LogWarning(ex, "Dropbox token refresh overlapped provider disposal");
            return false;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private HttpRequestMessage CreateUploadRequest(object uploadArgs, byte[] payload)
    {
        var request = CreateAuthorizedRequest(HttpMethod.Post, $"{DropboxContentUrl}/files/upload", _accessToken);
        request.Headers.Add("Dropbox-API-Arg", JsonSerializer.Serialize(uploadArgs));
        request.Content = new ByteArrayContent(payload);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(HostingConstants.BinaryContentType);
        return request;
    }

    private async Task<(HttpResponseMessage Response, string ErrorContent)> TryRetryUploadAfterRefreshAsync(
        HttpResponseMessage failedResponse,
        string errorContent,
        object uploadArgs,
        byte[] payload,
        string safeFileName,
        CancellationToken cancellationToken)
    {
        if (!await TryRefreshAccessTokenAsync(cancellationToken))
        {
            return (failedResponse, errorContent);
        }

        failedResponse.Dispose();
        logger.LogInformation("Retrying Dropbox upload of {File} after token refresh", safeFileName);
        using var retryRequest = CreateUploadRequest(uploadArgs, payload);
        var response = await _httpClient.SendAsync(retryRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var retryError = response.IsSuccessStatusCode
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken);
        return (response, retryError);
    }

    private async Task FetchRemainingPagesAsync(string initialCursor, HostingState state, CancellationToken cancellationToken)
    {
        var cursor = initialCursor;
        while (!string.IsNullOrEmpty(cursor))
        {
            cancellationToken.ThrowIfCancellationRequested();
            cursor = await ProcessRemainingPageAsync(cursor, state, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string?> ProcessRemainingPageAsync(string cursor, HostingState state, CancellationToken cancellationToken)
    {
        var continueArgs = new { cursor };
        using var continueRequest = CreateAuthorizedRequest(HttpMethod.Post, $"{DropboxApiUrl}/files/list_folder/continue", _accessToken);
        continueRequest.Content = new StringContent(
            JsonSerializer.Serialize(continueArgs),
            Encoding.UTF8,
            HostingConstants.JsonContentType);

        using var continueResponse = await _httpClient.SendAsync(continueRequest, cancellationToken).ConfigureAwait(false);
        if (!continueResponse.IsSuccessStatusCode)
        {
            var continueError = await continueResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            logger.LogWarning("Dropbox list_folder/continue failed: {Error}", continueError);
            return null;
        }

        var continueContent = await continueResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var continueDoc = JsonDocument.Parse(continueContent);
        if (continueDoc.RootElement.TryGetProperty("entries", out var continueEntries))
        {
            await ProcessEntriesAsync(continueEntries, state, cancellationToken).ConfigureAwait(false);
        }

        var hasMore = continueDoc.RootElement.TryGetProperty("has_more", out var nextHasMore) && nextHasMore.GetBoolean();
        return hasMore && continueDoc.RootElement.TryGetProperty("cursor", out var nextCursor) ? nextCursor.GetString() : null;
    }

    private async Task ProcessEntriesAsync(JsonElement entries, HostingState state, CancellationToken cancellationToken)
    {
        foreach (var entry in entries.EnumerateArray())
        {
            var entryResult = await ProcessDropboxEntryAsync(entry, state, cancellationToken).ConfigureAwait(false);
            if (!entryResult.Success)
            {
                logger.LogWarning("Skipping Dropbox entry during state recovery pagination: {Error}", entryResult.FirstError);
            }
        }
    }

    private async Task<OperationResult> ProcessDropboxEntryAsync(
        JsonElement entry,
        HostingState state,
        CancellationToken cancellationToken)
    {
        if (!entry.TryGetProperty(".tag", out var tagProp) || tagProp.GetString() != "file")
        {
            return OperationResult.CreateSuccess();
        }

        var fileName = entry.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
        var fileId = entry.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
        var fileSize = entry.TryGetProperty("size", out var sizeProp) && sizeProp.TryGetInt64(out var size) ? size : 0L;
        var lastUpdated = GetEntryTimestamp(entry);
        var pathLower = entry.TryGetProperty("path_lower", out var pathLowerProp)
            ? pathLowerProp.GetString() ?? $"{PublisherFolderPath}/{fileName}".ToLowerInvariant()
            : $"{PublisherFolderPath}/{fileName}".ToLowerInvariant();

        // Check for existing shared link without creating a new public link for unshared files during scan/recovery
        var linkResult = await TryGetExistingSharedLinkAsync(pathLower, cancellationToken).ConfigureAwait(false);
        if (linkResult is { Success: false })
        {
            logger.LogWarning("Failed to query shared link for {Path}: {Error}", pathLower, linkResult.FirstError);
            return OperationResult.CreateFailure(linkResult.FirstError ?? "Failed to query Dropbox shared link due to missing permissions.");
        }

        var directUrl = (linkResult is { Success: true, Data: not null }) ? ConvertToDirectDownloadUrl(linkResult.Data) : string.Empty;

        if (fileName.Equals("publisher.json", StringComparison.OrdinalIgnoreCase))
        {
            state.Definition = new HostedFileInfo
            {
                FileId = fileId,
                Url = directUrl,
                FileSize = fileSize,
                LastUpdated = lastUpdated,
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
                LastUpdated = lastUpdated,
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
                LastUpdated = lastUpdated,
            });
            logger.LogInformation("Discovered artifact '{File}' in Dropbox: {Url}", fileName, directUrl);
        }

        return OperationResult.CreateSuccess();
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
        var listResponse = await SendListSharedLinksWithRetryAsync(path, cancellationToken).ConfigureAwait(false);

        if (listResponse == null || !listResponse.IsSuccessStatusCode)
        {
            var listError = listResponse != null ? await listResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false) : "No response";
            logger.LogWarning("Dropbox list_shared_links returned {Status}: {Error}", listResponse?.StatusCode, listError);
            listResponse?.Dispose();
            return HandleListSharedLinksFailure(listError);
        }

        using (listResponse)
        {
            var listContent = await listResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ExtractExistingUrlFromResponse(listContent, path);
        }
    }

    private OperationResult<string>? ExtractExistingUrlFromResponse(string listContent, string path)
    {
        try
        {
            using var listDoc = JsonDocument.Parse(listContent);
            if (listDoc.RootElement.TryGetProperty("links", out var links) &&
                links.ValueKind == JsonValueKind.Array &&
                links.GetArrayLength() > 0 &&
                links[0].TryGetProperty("url", out var urlProp))
            {
                var existingUrl = urlProp.GetString();
                if (!string.IsNullOrEmpty(existingUrl))
                {
                    logger.LogInformation("Found existing Dropbox shared link: {Url}", existingUrl);
                    return OperationResult<string>.CreateSuccess(existingUrl);
                }
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse Dropbox shared link response for {Path}", path);
        }

        return null;
    }

    private async Task<HttpResponseMessage?> SendListSharedLinksWithRetryAsync(string path, CancellationToken cancellationToken)
    {
        var listRequest = new { path, direct_only = true };
        HttpResponseMessage? listResponse = null;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var listSharedLinksRequest = CreateAuthorizedRequest(HttpMethod.Post, $"{DropboxApiUrl}/sharing/list_shared_links", _accessToken);
            listSharedLinksRequest.Content = new StringContent(JsonSerializer.Serialize(listRequest), Encoding.UTF8, HostingConstants.JsonContentType);
            listResponse = await _httpClient.SendAsync(listSharedLinksRequest, cancellationToken).ConfigureAwait(false);

            if ((int)listResponse.StatusCode == 429 && attempt < 2)
            {
                var delaySeconds = listResponse.Headers.RetryAfter?.Delta?.TotalSeconds is { } s && s > 0 ? (int)Math.Min(s, 10) : 2;
                logger.LogWarning("Dropbox rate limit hit during shared link query for {Path}. Retrying in {Seconds}s...", path, delaySeconds);
                listResponse.Dispose();
                listResponse = null;
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken).ConfigureAwait(false);
                continue;
            }

            break;
        }

        return listResponse;
    }

    private async Task<OperationResult<string>> RequestNewSharedLinkAsync(string path, CancellationToken cancellationToken)
    {
        var createRequest = new { path };
        using var createLinkRequest = CreateAuthorizedRequest(HttpMethod.Post, $"{DropboxApiUrl}/sharing/create_shared_link_with_settings", _accessToken);
        createLinkRequest.Content = new StringContent(JsonSerializer.Serialize(createRequest), Encoding.UTF8, HostingConstants.JsonContentType);
        var response = await _httpClient.SendAsync(createLinkRequest, cancellationToken).ConfigureAwait(false);

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
