using GenHub.Core.Interfaces.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;
using GenHub.Features.Tools.Interfaces;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.Services.Hosting;

/// <summary>
/// Hosting provider for Google Drive.
/// Enables publishers to host catalogs and artifacts on Google Drive using OAuth 2.0.
/// </summary>
public class GoogleDriveHostingProvider : IHostingProvider
{
    private const string ApplicationName = "GenHub Publisher Studio";
    private const string PublisherFolderName = "GenHub_Publisher";
    private static readonly string[] Scopes = [DriveService.Scope.DriveFile];

    private readonly ILogger<GoogleDriveHostingProvider> _logger;
    private readonly IConfigurationProviderService? _configurationProvider;
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
    /// Initializes a new instance of the <see cref="GoogleDriveHostingProvider"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="configurationProvider">Optional configuration provider service for application data path resolution.</param>
    public GoogleDriveHostingProvider(
        ILogger<GoogleDriveHostingProvider> logger,
        IConfigurationProviderService? configurationProvider = null)
    {
        _logger = logger;
        _configurationProvider = configurationProvider;
    }

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
            _logger.LogInformation("Starting Google Drive authentication...");

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

            // Reject web client secrets if user accidentally pasted Web application credentials
            if (clientId.Contains("apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(clientSecret) &&
                clientSecret.StartsWith("GOCSPX-", StringComparison.OrdinalIgnoreCase))
            {
                // This is a typical Google OAuth client secret format
                _logger.LogDebug("Client credentials format verified");
            }

            var secrets = new ClientSecrets
            {
                ClientId = clientId,
                ClientSecret = clientSecret,
            };

            // Store credentials in the GenHub app data directory
            var baseDataPath = _configurationProvider?.GetApplicationDataPath()
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".genhub");
            var credPath = Path.Combine(baseDataPath, "google-drive-tokens");

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

            _logger.LogInformation("Successfully authenticated with Google Drive");
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Google Drive authentication was canceled or timed out.");
            return OperationResult<bool>.CreateFailure(
                "Google Drive authentication timed out or was canceled. " +
                "If your browser displayed 'Error 400: redirect_uri_mismatch', your OAuth Client ID was created as a 'Web application' instead of a 'Desktop app'. " +
                "In Google Cloud Console, delete this Client ID, create a new OAuth Client ID with Application type set to 'Desktop app', and try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to authenticate with Google Drive");
            var errorMsg = ex.Message;

            if (errorMsg.Contains("redirect_uri_mismatch", StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult<bool>.CreateFailure(
                    "Google OAuth Error (redirect_uri_mismatch): Your OAuth Client ID was created with Application type 'Web application'. " +
                    "Desktop applications cannot use web application credentials. " +
                    "Fix: In Google Cloud Console (APIs & Services -> Credentials), delete your current client ID, click 'Create Credentials' -> 'OAuth client ID', " +
                    "select Application type 'Desktop app', and use the newly generated Client ID and Client Secret.");
            }

            return OperationResult<bool>.CreateFailure($"Failed to authenticate with Google Drive: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task SignOutAsync()
    {
        _driveService?.Dispose();
        _driveService = null;
        _logger.LogInformation("Signed out from Google Drive");
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
            string parentFolderId;
            if (string.IsNullOrEmpty(folderPath))
            {
                var folderResult = await GetOrCreatePublisherFolderAsync(cancellationToken);
                if (!folderResult.Success)
                {
                    return OperationResult<HostingUploadResult>.CreateFailure(folderResult);
                }

                parentFolderId = folderResult.Data;
            }
            else
            {
                parentFolderId = folderPath;
            }

            var fileMetadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = fileName,
                Parents = new List<string> { parentFolderId },
            };

            var mimeType = GetMimeType(fileName);
            var uploadRequest = _driveService.Files.Create(fileMetadata, fileStream, mimeType);
            uploadRequest.Fields = "id, name, size, webViewLink, webContentLink";

            if (progress != null)
            {
                uploadRequest.ProgressChanged += uploadProgress =>
                {
                    if (uploadProgress.Status == UploadStatus.Uploading)
                    {
                        var percentage = (int)((double)uploadProgress.BytesSent / fileStream.Length * 100);
                        progress.Report(percentage);
                    }
                };
            }

            var uploadResult = await uploadRequest.UploadAsync(cancellationToken);

            if (uploadResult.Status != UploadStatus.Completed)
            {
                _logger.LogError("Google Drive upload failed: {Error}", uploadResult.Exception?.Message);
                return OperationResult<HostingUploadResult>.CreateFailure(
                    uploadResult.Exception?.Message ?? "Upload failed");
            }

            var uploadedFile = uploadRequest.ResponseBody;
            _logger.LogInformation("Uploaded {FileName} to Google Drive (ID: {FileId})", fileName, uploadedFile.Id);

            // Make the file publicly accessible
            await MakePublicAsync(uploadedFile.Id, cancellationToken);

            var directDownloadUrl = string.Format(
                HostingConstants.GoogleDriveDownloadUrlTemplate,
                uploadedFile.Id);

            return OperationResult<HostingUploadResult>.CreateSuccess(new HostingUploadResult
            {
                FileId = uploadedFile.Id,
                PublicUrl = uploadedFile.WebViewLink ?? directDownloadUrl,
                DirectDownloadUrl = directDownloadUrl,
                FileSize = uploadedFile.Size ?? fileStream.Length,
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading {FileName} to Google Drive", fileName);
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
                    if (uploadProgress.Status == UploadStatus.Uploading)
                    {
                        var percentage = (int)((double)uploadProgress.BytesSent / fileStream.Length * 100);
                        progress.Report(percentage);
                    }
                };
            }

            var uploadResult = await updateRequest.UploadAsync(cancellationToken);

            if (uploadResult.Status != UploadStatus.Completed)
            {
                _logger.LogError("Google Drive update failed for {FileId}: {Error}", fileId, uploadResult.Exception?.Message);
                return OperationResult<HostingUploadResult>.CreateFailure(
                    uploadResult.Exception?.Message ?? "Update failed");
            }

            var updatedFile = updateRequest.ResponseBody;
            _logger.LogInformation("Updated file {FileId} on Google Drive", fileId);

            var directDownloadUrl = string.Format(
                HostingConstants.GoogleDriveDownloadUrlTemplate,
                fileId);

            return OperationResult<HostingUploadResult>.CreateSuccess(new HostingUploadResult
            {
                FileId = fileId,
                PublicUrl = updatedFile.WebViewLink ?? directDownloadUrl,
                DirectDownloadUrl = directDownloadUrl,
                FileSize = updatedFile.Size ?? fileStream.Length,
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating file {FileId} on Google Drive", fileId);
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
            listRequest.Q = $"name = '{PublisherFolderName}' and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
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
            _logger.LogInformation("Created Google Drive publisher folder with ID: {FolderId}", folder.Id);

            // Make the folder publicly readable so files inside inherit read access
            await MakePublicAsync(folder.Id, cancellationToken);

            return OperationResult<string>.CreateSuccess(folder.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting or creating Google Drive folder");
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
            var folderResult = await GetOrCreatePublisherFolderAsync(cancellationToken);
            if (!folderResult.Success)
            {
                return OperationResult<HostingState?>.CreateFailure(folderResult);
            }

            var folderId = folderResult.Data;
            var listRequest = _driveService.Files.List();
            listRequest.Q = $"'{folderId}' in parents and trashed = false";
            listRequest.Fields = "files(id, name, size, modifiedTime, webViewLink)";

            var result = await listRequest.ExecuteAsync(cancellationToken);

            var state = new HostingState
            {
                ProviderId = ProviderId,
                FolderId = folderId,
                FolderUrl = $"https://drive.google.com/drive/folders/{folderId}",
                LastPublished = DateTime.UtcNow,
            };

            if (result.Files != null)
            {
                foreach (var file in result.Files)
                {
                    ProcessGoogleDriveFile(file, state);
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
            _logger.LogError(ex, "Error scanning Google Drive for publisher files");
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

        return url.Contains("drive.google.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
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
        // Handle https://drive.google.com/file/d/{fileId}/view
        var fileDIndex = url.IndexOf("/file/d/", StringComparison.OrdinalIgnoreCase);
        if (fileDIndex >= 0)
        {
            var start = fileDIndex + "/file/d/".Length;
            var end = url.IndexOf('/', start);
            return end >= 0 ? url[start..end] : url[start..];
        }

        // Handle https://drive.google.com/open?id={fileId}
        var idIndex = url.IndexOf("id=", StringComparison.OrdinalIgnoreCase);
        if (idIndex >= 0)
        {
            var start = idIndex + "id=".Length;
            var end = url.IndexOf('&', start);
            return end >= 0 ? url[start..end] : url[start..];
        }

        return null;
    }

    private async Task MakePublicAsync(string fileId, CancellationToken cancellationToken)
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
            _logger.LogDebug("Made file/folder public: {FileId}", fileId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set public permission for {FileId}", fileId);
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
            _logger.LogInformation("Discovered publisher definition on Google Drive: {Url}", directUrl);
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
            _logger.LogInformation("Discovered catalog '{CatalogId}' on Google Drive: {Url}", catId, directUrl);
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
            _logger.LogInformation("Discovered artifact '{File}' on Google Drive: {Url}", file.Name, directUrl);
        }
    }
}
