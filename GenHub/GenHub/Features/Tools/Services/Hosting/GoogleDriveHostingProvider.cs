using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;
using GenHub.Features.Tools.Interfaces;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.Services.Hosting;

/// <summary>
/// Hosting provider for Google Drive.
/// Enables publishers to host catalogs and artifacts on Google Drive using OAuth 2.0.
/// </summary>
public class GoogleDriveHostingProvider(
    ILogger<GoogleDriveHostingProvider> logger,
    IConfigurationProviderService? configurationProvider = null,
    ILocalizationService? localizationService = null,
    IHostingCredentialStore? credentialStore = null) : IHostingProvider
{
    private const string ApplicationName = "GenHub Publisher Studio";
    private const string PublisherFolderName = HostingConstants.GoogleDriveDefaultPublisherFolder;
    private static readonly string[] Scopes = [DriveService.Scope.DriveFile];

    private static readonly Regex GoogleDriveIdRegex = new(
        @"(?:/file/d/|[?&]id=)([a-zA-Z0-9_-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    private static readonly Regex GoogleConsoleUrlRegex = new(
        @"https?://console\.(?:developers|cloud)\.google\.com[^\s]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    private DriveService? _driveService;

    /// <summary>
    /// Gets the maximum file size supported by Google Drive.
    /// </summary>
    public static long MaxFileSizeBytes => 5L * 1024 * 1024 * 1024 * 1024; // 5TB Google Drive limit

    /// <summary>
    /// Gets or sets a custom client ID configured by the user via the UI.
    /// </summary>
    public string? CustomClientId { get; set; }

    /// <summary>
    /// Gets or sets a custom client secret configured by the user via the UI.
    /// </summary>
    public string? CustomClientSecret { get; set; }

    /// <inheritdoc />
    public string ProviderId => HostingConstants.GoogleDrive;

    /// <inheritdoc />
    public string DisplayName => "Google Drive";

    /// <inheritdoc />
    public string Description => "Host your catalogs and artifacts on Google Drive. 15GB free storage, reliable downloads.";

    /// <inheritdoc />
    public string IconName => "GoogleDrive";

    /// <inheritdoc />
    public bool RequiresAuthentication => true;

    /// <inheritdoc />
    public bool IsAuthenticated => _driveService != null;

    /// <inheritdoc />
    public bool SupportsCatalogHosting => true;

    /// <inheritdoc />
    public bool SupportsArtifactHosting => true;

    /// <inheritdoc />
    public bool SupportsUpdate => true;

    /// <summary>
    /// Authenticates with Google Drive using OAuth 2.0 authorization code flow.
    /// Opens the system browser for user consent.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success or failure.</returns>
    public async Task<OperationResult<bool>> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Starting Google Drive authentication...");

            // Use custom credentials if provided by the user
            var clientId = CustomClientId?.Trim();
            var clientSecret = CustomClientSecret?.Trim();

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                return OperationResult<bool>.CreateFailure(
                    "Google Drive OAuth credentials are required. " +
                    "In Google Cloud Console, first create or select a project, set up your OAuth consent screen under 'APIs & Services' -> 'OAuth consent screen', " +
                    "then navigate to 'Credentials' -> 'Create Credentials' -> 'OAuth client ID', select Application type: 'Desktop app', " +
                    "and paste your Client ID and Client Secret into the fields above.");
            }

            var secrets = new ClientSecrets
            {
                ClientId = clientId,
                ClientSecret = clientSecret,
            };

            var dataStore = CreateTokenDataStore();

            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets,
                Scopes,
                "user",
                cancellationToken,
                dataStore);

            DeleteLegacyPlaintextTokenStore();

            _driveService = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });

            logger.LogInformation("Successfully authenticated with Google Drive");
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning(ex, "Google Drive authentication was canceled or timed out.");
            return OperationResult<bool>.CreateFailure(
                "Google Drive authentication was canceled or timed out.\n\n" +
                "• If your browser displayed 'Error 403: access_denied' (Access blocked: GenHub has not completed the Google verification process):\n" +
                "  Your Google Cloud project is in 'Testing' mode. In Google Cloud Console, open 'Audience' (or 'OAuth consent screen') > 'Test users', click '+ ADD USERS', enter your Google account email, and click 'SAVE'.\n\n" +
                "• If your browser displayed 'Error 400: redirect_uri_mismatch':\n" +
                "  Your OAuth Client ID was created as a 'Web application' instead of a 'Desktop app'. In Google Cloud Console > Credentials, delete it and create a new OAuth Client ID with Application type set to 'Desktop app'.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to authenticate with Google Drive");
            var errorMsg = ex.Message;
            if (errorMsg.Contains("access_denied", StringComparison.OrdinalIgnoreCase))
            {
                errorMsg = "Access was denied (Error 403: access_denied). Your Google Cloud project is in Testing mode. Go to Google Cloud Console > 'Audience' (or 'OAuth consent screen') > 'Test users', click '+ ADD USERS', and add your Google account email.";
            }

            return OperationResult<bool>.CreateFailure($"Authentication failed: {errorMsg}");
        }
        finally
        {
            CustomClientSecret = null;
        }
    }

    /// <inheritdoc />
    public async Task SignOutAsync()
    {
        _driveService?.Dispose();
        _driveService = null;

        try
        {
            await CreateTokenDataStore().ClearAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to clear the stored Google Drive token during sign-out.");
        }

        logger.LogInformation("Signed out from Google Drive");
    }

    /// <inheritdoc />
    public async Task<OperationResult<HostingUploadResult>> UploadFileAsync(
        Stream fileStream,
        string fileName,
        string? folderPath = null,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_driveService == null)
        {
            return OperationResult<HostingUploadResult>.CreateFailure(GetNotAuthenticatedMessage());
        }

        try
        {
            // Ensure publisher folder exists
            var folderResult = await GetOrCreatePublisherFolderAsync(cancellationToken);
            if (!folderResult.Success || string.IsNullOrEmpty(folderResult.Data))
            {
                var error = folderResult.FirstError;
                if (IsApiNotEnabledMessage(error))
                {
                    return OperationResult<HostingUploadResult>.CreateFailure(error!);
                }

                return OperationResult<HostingUploadResult>.CreateFailure(GetPublisherFolderFailedMessage(error));
            }

            var folderId = folderResult.Data;

            // Check if file already exists in the folder
            var existingFile = await FindExistingFileAsync(fileName, folderId, cancellationToken);
            if (existingFile != null)
            {
                // Update existing file
                return await UpdateFileAsync(existingFile.Id, fileStream, fileName, progress, cancellationToken);
            }

            // Create new file
            var fileMetadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = fileName,
                Parents = [folderId],
            };

            var mimeType = GetMimeType(fileName);
            var insertRequest = _driveService.Files.Create(fileMetadata, fileStream, mimeType);
            insertRequest.Fields = "id, name, size, webViewLink, webContentLink";

            AttachUploadProgress(insertRequest, fileStream, progress);

            var uploadResult = await insertRequest.UploadAsync(cancellationToken);

            if (uploadResult.Status != UploadStatus.Completed)
            {
                logger.LogError("Google Drive upload failed for {FileName}: {Error}", fileName, uploadResult.Exception?.Message);
                return OperationResult<HostingUploadResult>.CreateFailure(
                    uploadResult.Exception?.Message ?? "Upload failed");
            }

            var uploadedFile = insertRequest.ResponseBody;
            logger.LogInformation("Successfully uploaded {FileName} to Google Drive. ID: {FileId}", fileName, uploadedFile.Id);

            // Make the file publicly readable
            var permResult = await MakePublicAsync(uploadedFile.Id, cancellationToken);
            if (!permResult.Success)
            {
                logger.LogWarning("File {FileName} uploaded but failed to make public: {Error}", fileName, permResult.FirstError);
                return OperationResult<HostingUploadResult>.CreateFailure(
                    $"File '{fileName}' was uploaded to Google Drive, but setting public permissions failed: {permResult.FirstError}");
            }

            var directDownloadUrl = string.Format(
                HostingConstants.GoogleDriveDownloadUrlTemplate,
                uploadedFile.Id);

            return OperationResult<HostingUploadResult>.CreateSuccess(new HostingUploadResult
            {
                FileId = uploadedFile.Id,
                PublicUrl = uploadedFile.WebViewLink ?? directDownloadUrl,
                DirectDownloadUrl = directDownloadUrl,
                FileSize = uploadedFile.Size ?? (fileStream.CanSeek ? fileStream.Length : 0),
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to upload {FileName} to Google Drive", fileName);
            return OperationResult<HostingUploadResult>.CreateFailure(GetUserFacingApiErrorMessage(ex, "Upload error"));
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<HostingUploadResult>> UploadCatalogAsync(
        string catalogJson,
        string publisherId,
        string? catalogFileName = null,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var fileName = string.IsNullOrWhiteSpace(catalogFileName) ? $"catalog-{publisherId}.json" : catalogFileName;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(catalogJson));
        return await UploadFileAsync(stream, fileName, null, progress, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<HostingUploadResult>> UpdateFileAsync(
        string fileId,
        Stream fileStream,
        string fileName,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_driveService == null)
        {
            return OperationResult<HostingUploadResult>.CreateFailure(GetNotAuthenticatedMessage());
        }

        try
        {
            var fileMetadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = fileName,
            };

            var mimeType = GetMimeType(fileName);
            var updateRequest = _driveService.Files.Update(fileMetadata, fileId, fileStream, mimeType);
            updateRequest.Fields = "id, name, size, webViewLink, webContentLink";

            if (progress != null)
            {
                updateRequest.ProgressChanged += uploadProgress =>
                {
                    if (uploadProgress.Status == UploadStatus.Uploading && fileStream.CanSeek && fileStream.Length > 0)
                    {
                        var percentage = (int)((double)uploadProgress.BytesSent / fileStream.Length * 100);
                        progress.Report(percentage);
                    }
                };
            }

            var uploadResult = await updateRequest.UploadAsync(cancellationToken);

            if (uploadResult.Status != UploadStatus.Completed)
            {
                logger.LogError("Google Drive update failed for {FileId}: {Error}", fileId, uploadResult.Exception?.Message);
                return OperationResult<HostingUploadResult>.CreateFailure(
                    uploadResult.Exception?.Message ?? "Update failed");
            }

            var updatedFile = updateRequest.ResponseBody;
            logger.LogInformation("Updated file {FileId} on Google Drive", fileId);

            // Ensure public permissions
            var permResult = await MakePublicAsync(fileId, cancellationToken);
            if (!permResult.Success)
            {
                logger.LogWarning("File {FileId} updated but failed to make public: {Error}", fileId, permResult.FirstError);
                return OperationResult<HostingUploadResult>.CreateFailure(
                    $"File '{fileName}' was updated on Google Drive, but setting public permissions failed: {permResult.FirstError}");
            }

            var directDownloadUrl = string.Format(
                HostingConstants.GoogleDriveDownloadUrlTemplate,
                updatedFile.Id);

            return OperationResult<HostingUploadResult>.CreateSuccess(new HostingUploadResult
            {
                FileId = updatedFile.Id,
                PublicUrl = updatedFile.WebViewLink ?? directDownloadUrl,
                DirectDownloadUrl = directDownloadUrl,
                FileSize = updatedFile.Size ?? (fileStream.CanSeek ? fileStream.Length : 0),
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update file {FileId} on Google Drive", fileId);
            return OperationResult<HostingUploadResult>.CreateFailure(GetUserFacingApiErrorMessage(ex, "Update error"));
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> DeleteFileAsync(string fileId, CancellationToken cancellationToken = default)
    {
        if (_driveService == null)
        {
            return OperationResult<bool>.CreateFailure(GetNotAuthenticatedMessage());
        }

        if (string.IsNullOrWhiteSpace(fileId))
        {
            return OperationResult<bool>.CreateFailure("A Google Drive file ID is required to delete a file.");
        }

        try
        {
            await _driveService.Files.Delete(fileId.Trim()).ExecuteAsync(cancellationToken);
            logger.LogInformation("Deleted Google Drive file {FileId}", fileId);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            logger.LogInformation(ex, "Google Drive file {FileId} was already deleted.", fileId);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete Google Drive file {FileId}", fileId);
            return OperationResult<bool>.CreateFailure(GetUserFacingApiErrorMessage(ex, "Google Drive delete error"));
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<string>> GetOrCreatePublisherFolderAsync(CancellationToken cancellationToken = default)
    {
        if (_driveService == null)
        {
            return OperationResult<string>.CreateFailure(GetNotAuthenticatedMessage());
        }

        try
        {
            var existingFolderId = await FindPublisherFolderIdAsync(cancellationToken);
            if (existingFolderId != null)
            {
                return OperationResult<string>.CreateSuccess(existingFolderId);
            }

            // Create new folder
            var folderMetadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = PublisherFolderName,
                MimeType = "application/vnd.google-apps.folder",
            };

            var createRequest = _driveService.Files.Create(folderMetadata);
            createRequest.Fields = "id";

            try
            {
                var folder = await createRequest.ExecuteAsync(cancellationToken);
                logger.LogInformation("Created Google Drive publisher folder with ID: {FolderId}", folder.Id);

                // Files uploaded through this provider receive their own public reader
                // permission, so the folder itself stays private.
                return OperationResult<string>.CreateSuccess(folder.Id);
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Conflict)
            {
                // A concurrent client created the folder first; re-query instead of failing.
                logger.LogInformation(ex, "Publisher folder was created concurrently; reusing existing folder.");
                var concurrentFolderId = await FindPublisherFolderIdAsync(cancellationToken);
                return concurrentFolderId != null
                    ? OperationResult<string>.CreateSuccess(concurrentFolderId)
                    : OperationResult<string>.CreateFailure("Folder operation failed: publisher folder conflict could not be resolved.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting or creating Google Drive folder");
            return OperationResult<string>.CreateFailure(GetUserFacingApiErrorMessage(ex, "Folder operation failed"));
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<HostingState?>> RecoverHostingStateAsync(CancellationToken cancellationToken = default)
    {
        if (_driveService == null)
        {
            return OperationResult<HostingState?>.CreateFailure(GetNotAuthenticatedMessage());
        }

        try
        {
            var listFolderRequest = _driveService.Files.List();
            listFolderRequest.Q = $"name = '{EscapeDriveQueryParameter(PublisherFolderName)}' and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
            listFolderRequest.Fields = "files(id, name)";

            var folderListResult = await listFolderRequest.ExecuteAsync(cancellationToken);
            var existingFolder = folderListResult.Files?.FirstOrDefault();
            if (existingFolder == null)
            {
                return OperationResult<HostingState?>.CreateSuccess(null);
            }

            var folderId = existingFolder.Id;

            // LastPublished is intentionally left unset: recovery rediscovers existing
            // state rather than publishing, so there is no meaningful publish time.
            var state = new HostingState
            {
                ProviderId = ProviderId,
                FolderId = folderId,
                FolderUrl = string.Format(HostingConstants.GoogleDriveFolderUrlTemplate, folderId),
            };

            string? pageToken = null;
            do
            {
                var listRequest = _driveService.Files.List();
                listRequest.Q = $"'{EscapeDriveQueryParameter(folderId)}' in parents and trashed = false";
                listRequest.Fields = "nextPageToken, files(id, name, size, modifiedTime, webViewLink)";
                listRequest.PageToken = pageToken;
                listRequest.PageSize = 100;

                var result = await listRequest.ExecuteAsync(cancellationToken);
                if (result.Files != null)
                {
                    foreach (var file in result.Files)
                    {
                        ProcessGoogleDriveFile(file, state);
                    }
                }

                pageToken = result.NextPageToken;
            }
            while (!string.IsNullOrEmpty(pageToken));

            return OperationResult<HostingState?>.CreateSuccess(state);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error scanning Google Drive for publisher files");
            return OperationResult<HostingState?>.CreateFailure(GetUserFacingApiErrorMessage(ex, "Google Drive scan error"));
        }
    }

    /// <inheritdoc />
    public string GetSubscriptionLink(string catalogUrl)
    {
        return CommandLineConstants.BuildSubscriptionUrl(catalogUrl);
    }

    /// <inheritdoc />
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
            (IsDriveHost(uri.Host, "drive.google.com") || IsDriveHost(uri.Host, "docs.google.com"));
    }

    /// <inheritdoc />
    // skipcq: CS-A1000
    public string GetDirectDownloadUrl(string shareUrl)
    {
        if (string.IsNullOrEmpty(shareUrl))
        {
            return string.Empty;
        }

        if (shareUrl.Contains("drive.google.com/uc?", StringComparison.OrdinalIgnoreCase))
        {
            return shareUrl;
        }

        var fileId = ExtractFileId(shareUrl);
        return !string.IsNullOrEmpty(fileId)
            ? string.Format(HostingConstants.GoogleDriveDownloadUrlTemplate, fileId)
            : shareUrl;
    }

    private static string EscapeDriveQueryParameter(string input)
    {
        return input.Replace("\\", "\\\\").Replace("'", "\\'");
    }

    private static string GetMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".json" => HostingConstants.JsonContentType,
            ".zip" => "application/zip",
            ".exe" => "application/x-msdownload",
            ".7z" => "application/x-7z-compressed",
            ".tar" => "application/x-tar",
            ".gz" => "application/gzip",
            _ => HostingConstants.BinaryContentType,
        };
    }

    private static bool IsDriveHost(string host, string domain)
    {
        return host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractFileId(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var match = GoogleDriveIdRegex.Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static void AttachUploadProgress(
        Google.Apis.Upload.ResumableUpload<Google.Apis.Drive.v3.Data.File> insertRequest,
        Stream fileStream,
        IProgress<int>? progress)
    {
        if (progress == null)
        {
            return;
        }

        insertRequest.ProgressChanged += uploadProgress =>
        {
            if (uploadProgress.Status == UploadStatus.Uploading && fileStream.CanSeek && fileStream.Length > 0)
            {
                var percentage = (int)((double)uploadProgress.BytesSent / fileStream.Length * 100);
                progress.Report(percentage);
            }
        };
    }

    private static bool IsApiDisabledError(Exception ex)
    {
        if (ex is Google.GoogleApiException apiEx &&
            apiEx.HttpStatusCode == HttpStatusCode.Forbidden &&
            apiEx.Error?.Errors != null &&
            apiEx.Error.Errors.Any(e => string.Equals(e.Reason, "accessNotConfigured", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var msg = ex.Message ?? string.Empty;
        var mentionsDriveApi = msg.Contains("Google Drive API", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("drive.googleapis.com", StringComparison.OrdinalIgnoreCase);
        return mentionsDriveApi &&
            (msg.Contains("disabled", StringComparison.OrdinalIgnoreCase) ||
             msg.Contains("not been used", StringComparison.OrdinalIgnoreCase) ||
             msg.Contains("accessNotConfigured", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Detects API-disabled failures from their message using the locale-invariant console URL,
    /// so callers can recognize them without matching untranslated text.
    /// </summary>
    /// <param name="message">The failure message to inspect.</param>
    /// <returns><c>true</c> when the message is an API-disabled failure; otherwise, <c>false</c>.</returns>
    private static bool IsApiNotEnabledMessage(string? message)
    {
        return !string.IsNullOrEmpty(message) &&
            (message.Contains(HostingConstants.GoogleDriveApiEnablementUrl, StringComparison.OrdinalIgnoreCase) ||
             GoogleConsoleUrlRegex.IsMatch(message));
    }

    private string GetUserFacingApiErrorMessage(Exception ex, string operationFallback)
    {
        if (IsApiDisabledError(ex))
        {
            var msg = ex.Message ?? string.Empty;
            var match = GoogleConsoleUrlRegex.Match(msg);
            var consoleUrl = match.Success
                ? match.Value.TrimEnd('.', ',', ';', ')')
                : HostingConstants.GoogleDriveApiEnablementUrl;

            if (localizationService != null &&
                localizationService.TryGetString("Tools.PublisherStudio.Hosting.GoogleDriveApiNotEnabled", out var localized, consoleUrl))
            {
                return localized;
            }

            return $"The Google Drive API is not enabled for your Google Cloud project. Enable it at {consoleUrl}, wait a few minutes for the change to propagate, then retry.";
        }

        return $"{operationFallback}: {ex.Message}";
    }

    private string GetNotAuthenticatedMessage() =>
        localizationService != null && localizationService.TryGetString("Tools.PublisherStudio.Hosting.GoogleDriveNotAuthenticated", out var localized)
            ? localized
            : HostingConstants.GoogleDriveNotAuthenticated;

    private string GetPublisherFolderFailedMessage(string? error)
    {
        var folderFailed = localizationService?.TryGetString("Tools.PublisherStudio.Hosting.PublisherFolderFailed", out var folderFailedMessage) == true
            ? folderFailedMessage
            : "Failed to get publisher folder";
        if (error == null)
        {
            return folderFailed;
        }

        if (localizationService != null &&
            localizationService.TryGetString("Tools.PublisherStudio.Hosting.PublisherFolderFailedFormat", out var folderFailedFormat, error))
        {
            return folderFailedFormat;
        }

        return $"{folderFailed}: {error}";
    }

    private IDataStore CreateTokenDataStore()
    {
        if (credentialStore != null)
        {
            return new CredentialStoreDataStore(credentialStore);
        }

        // Fallback for hosts without a credential store (e.g. unit tests)
        var baseDataPath = configurationProvider?.GetApplicationDataPath()
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".genhub");
        var credPath = Path.Combine(baseDataPath, HostingConstants.GoogleDriveTokenDirectoryName);
        return new FileDataStore(credPath, true);
    }

    private string GetLegacyTokenStorePath()
    {
        var baseDataPath = configurationProvider?.GetApplicationDataPath()
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".genhub");
        return Path.Combine(baseDataPath, HostingConstants.GoogleDriveTokenDirectoryName);
    }

    private void DeleteLegacyPlaintextTokenStore()
    {
        if (credentialStore == null)
        {
            return;
        }

        try
        {
            var legacyPath = GetLegacyTokenStorePath();
            if (Directory.Exists(legacyPath))
            {
                Directory.Delete(legacyPath, true);
                logger.LogInformation("Removed legacy plaintext Google Drive token store after migrating to the secure credential store.");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to remove the legacy plaintext Google Drive token store. Please delete it manually.");
        }
    }

    private async Task<string?> FindPublisherFolderIdAsync(CancellationToken cancellationToken)
    {
        if (_driveService == null)
        {
            return null;
        }

        var listRequest = _driveService.Files.List();
        listRequest.Q = $"name = '{EscapeDriveQueryParameter(PublisherFolderName)}' and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
        listRequest.Fields = "files(id, name)";

        var listResult = await listRequest.ExecuteAsync(cancellationToken);
        return listResult.Files?.FirstOrDefault()?.Id;
    }

    private async Task<OperationResult<bool>> MakePublicAsync(string fileId, CancellationToken cancellationToken)
    {
        var service = _driveService;
        if (service == null)
        {
            return OperationResult<bool>.CreateFailure(GetNotAuthenticatedMessage());
        }

        try
        {
            var permission = new Google.Apis.Drive.v3.Data.Permission
            {
                Type = "anyone",
                Role = "reader",
            };

            var permRequest = service.Permissions.Create(permission, fileId);
            await permRequest.ExecuteAsync(cancellationToken);
            logger.LogDebug("Made file/folder public: {FileId}", fileId);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to set public permission for {FileId}", fileId);
            return OperationResult<bool>.CreateFailure(GetUserFacingApiErrorMessage(ex, "Failed to set public permission"));
        }
    }

    private void ProcessGoogleDriveFile(Google.Apis.Drive.v3.Data.File file, HostingState state)
    {
        var directUrl = string.Format(HostingConstants.GoogleDriveDownloadUrlTemplate, file.Id);
        var fileSize = file.Size ?? 0;
        var lastUpdated = file.ModifiedTimeDateTimeOffset?.UtcDateTime ?? DateTime.UtcNow;

        if (file.Name.Equals("publisher.json", StringComparison.OrdinalIgnoreCase))
        {
            state.Definition = new HostedFileInfo
            {
                FileId = file.Id,
                Url = directUrl,
                FileSize = fileSize,
                LastUpdated = lastUpdated,
            };
            logger.LogInformation("Discovered publisher definition on Google Drive: {Url}", directUrl);
        }
        else if (file.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && file.Name.StartsWith("catalog-", StringComparison.OrdinalIgnoreCase))
        {
            var catId = file.Name.Replace("catalog-", string.Empty).Replace(".json", string.Empty);
            state.Catalogs.Add(new CatalogHostingInfo
            {
                FileId = file.Id,
                CatalogId = catId,
                FileName = file.Name,
                CatalogName = catId,
                Url = directUrl,
                FileSize = fileSize,
                LastUpdated = lastUpdated,
            });
            logger.LogInformation("Discovered catalog '{CatalogId}' on Google Drive: {Url}", catId, directUrl);
        }
        else
        {
            state.Artifacts.Add(new ArtifactHostingInfo
            {
                FileId = file.Id,
                FileName = file.Name,
                Url = directUrl,
                FileSize = fileSize,
                LastUpdated = lastUpdated,
            });
            logger.LogInformation("Discovered artifact '{File}' on Google Drive: {Url}", file.Name, directUrl);
        }
    }

    private async Task<Google.Apis.Drive.v3.Data.File?> FindExistingFileAsync(
        string fileName,
        string folderId,
        CancellationToken cancellationToken)
    {
        if (_driveService == null)
        {
            return null;
        }

        string? searchPageToken = null;
        do
        {
            var searchRequest = _driveService.Files.List();
            searchRequest.Q = $"name = '{EscapeDriveQueryParameter(fileName)}' and '{EscapeDriveQueryParameter(folderId)}' in parents and trashed = false";
            searchRequest.Fields = "nextPageToken, files(id, name)";
            searchRequest.PageToken = searchPageToken;
            searchRequest.PageSize = 100;
            var searchResult = await searchRequest.ExecuteAsync(cancellationToken);
            var existingFile = searchResult.Files?.FirstOrDefault();
            if (existingFile != null)
            {
                return existingFile;
            }

            searchPageToken = searchResult.NextPageToken;
        }
        while (!string.IsNullOrEmpty(searchPageToken));

        return null;
    }
}
