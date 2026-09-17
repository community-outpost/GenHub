using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
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
    IConfigurationProviderService? configurationProvider = null) : IHostingProvider
{
    private const string ApplicationName = "GenHub Publisher Studio";
    private const string PublisherFolderName = HostingConstants.GoogleDriveDefaultPublisherFolder;
    private static readonly string[] Scopes = [DriveService.Scope.DriveFile];

    private static readonly Regex GoogleDriveIdRegex = new(
        @"(?:/file/d/|[?&]id=)([a-zA-Z0-9_-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    private DriveService? _driveService;

    private static string EscapeDriveQueryParameter(string input)
    {
        return input.Replace("\\", "\\\\").Replace("'", "\\'");
    }

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

            // Store credentials in the GenHub app data directory
            var baseDataPath = configurationProvider?.GetApplicationDataPath()
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".genhub");
            var credPath = Path.Combine(baseDataPath, HostingConstants.GoogleDriveTokenDirectoryName);

            var dataStore = new FileDataStore(credPath, true);

            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets,
                Scopes,
                "user",
                cancellationToken,
                dataStore);

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
    }

    /// <inheritdoc />
    public Task SignOutAsync()
    {
        _driveService?.Dispose();
        _driveService = null;
        logger.LogInformation("Signed out from Google Drive");
        return Task.CompletedTask;
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
            return OperationResult<HostingUploadResult>.CreateFailure(HostingConstants.GoogleDriveNotAuthenticated);
        }

        try
        {
            // Ensure publisher folder exists
            var folderResult = await GetOrCreatePublisherFolderAsync(cancellationToken);
            if (!folderResult.Success || string.IsNullOrEmpty(folderResult.Data))
            {
                return OperationResult<HostingUploadResult>.CreateFailure(
                    $"Failed to get publisher folder: {folderResult.FirstError}");
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

            if (progress != null)
            {
                insertRequest.ProgressChanged += uploadProgress =>
                {
                    if (uploadProgress.Status == UploadStatus.Uploading && fileStream.CanSeek && fileStream.Length > 0)
                    {
                        var percentage = (int)((double)uploadProgress.BytesSent / fileStream.Length * 100);
                        progress.Report(percentage);
                    }
                };
            }

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
            return OperationResult<HostingUploadResult>.CreateFailure($"Upload error: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<HostingUploadResult>> UploadCatalogAsync(
        string catalogJson,
        string publisherId,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var fileName = $"catalog-{publisherId}.json";
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
            return OperationResult<HostingUploadResult>.CreateFailure(HostingConstants.GoogleDriveNotAuthenticated);
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
            return OperationResult<HostingUploadResult>.CreateFailure($"Update error: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<string>> GetOrCreatePublisherFolderAsync(CancellationToken cancellationToken = default)
    {
        if (_driveService == null)
        {
            return OperationResult<string>.CreateFailure(HostingConstants.GoogleDriveNotAuthenticated);
        }

        try
        {
            // Search for existing folder
            var listRequest = _driveService.Files.List();
            listRequest.Q = $"name = '{EscapeDriveQueryParameter(PublisherFolderName)}' and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
            listRequest.Fields = "files(id, name)";

            var listResult = await listRequest.ExecuteAsync(cancellationToken);
            var existingFolder = listResult.Files?.FirstOrDefault();

            if (existingFolder != null)
            {
                return OperationResult<string>.CreateSuccess(existingFolder.Id);
            }

            // Create new folder
            var folderMetadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = PublisherFolderName,
                MimeType = "application/vnd.google-apps.folder",
            };

            var createRequest = _driveService.Files.Create(folderMetadata);
            createRequest.Fields = "id";

            var folder = await createRequest.ExecuteAsync(cancellationToken);
            logger.LogInformation("Created Google Drive publisher folder with ID: {FolderId}", folder.Id);

            // Make the folder publicly readable so files inside inherit read access
            var folderPermResult = await MakePublicAsync(folder.Id, cancellationToken);
            if (!folderPermResult.Success)
            {
                logger.LogWarning("Google Drive publisher folder created, but setting public permission failed: {Error}", folderPermResult.FirstError);
            }

            return OperationResult<string>.CreateSuccess(folder.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting or creating Google Drive folder");
            return OperationResult<string>.CreateFailure($"Folder operation failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<HostingState?>> RecoverHostingStateAsync(CancellationToken cancellationToken = default)
    {
        if (_driveService == null)
        {
            return OperationResult<HostingState?>.CreateFailure(HostingConstants.GoogleDriveNotAuthenticated);
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
            var state = new HostingState
            {
                ProviderId = ProviderId,
                FolderId = folderId,
                FolderUrl = $"https://drive.google.com/drive/folders/{folderId}",
                LastPublished = DateTime.UtcNow,
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
            return OperationResult<HostingState?>.CreateFailure($"Google Drive scan error: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public string GetSubscriptionLink(string catalogUrl)
    {
        var encodedUrl = Uri.EscapeDataString(catalogUrl);
        return $"genhub://subscribe?url={encodedUrl}";
    }

    /// <inheritdoc />
    public bool IsValidHostingUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return false;
        }

        return url.Contains("drive.google.com", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("docs.google.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    // skipcq: CS-A1000
    public string GetDirectDownloadUrl(string shareUrl)
    {
        if (shareUrl.Contains("drive.google.com/uc?", StringComparison.OrdinalIgnoreCase))
        {
            return shareUrl;
        }

        var fileId = ExtractFileId(shareUrl);
        return !string.IsNullOrEmpty(fileId)
            ? string.Format(HostingConstants.GoogleDriveDownloadUrlTemplate, fileId)
            : shareUrl;
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

    private static string? ExtractFileId(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var match = GoogleDriveIdRegex.Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    private async Task<OperationResult<bool>> MakePublicAsync(string fileId, CancellationToken cancellationToken)
    {
        try
        {
            var permission = new Google.Apis.Drive.v3.Data.Permission
            {
                Type = "anyone",
                Role = "reader",
            };

            var permRequest = _driveService!.Permissions.Create(permission, fileId);
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
            return OperationResult<bool>.CreateFailure($"Failed to set public permission: {ex.Message}");
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
