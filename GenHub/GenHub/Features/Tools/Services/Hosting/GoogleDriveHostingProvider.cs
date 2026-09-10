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
    private DriveService? _driveService;

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
    /// Gets the maximum file size supported by Google Drive.
    /// </summary>
    public long MaxFileSizeBytes => 5L * 1024 * 1024 * 1024 * 1024; // 5TB Google Drive limit

    /// <summary>
    /// Initializes a new instance of the <see cref="GoogleDriveHostingProvider"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public GoogleDriveHostingProvider(ILogger<GoogleDriveHostingProvider> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Authenticates with Google Drive using OAuth 2.0.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success.</returns>
    public async Task<OperationResult<bool>> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting Google Drive authentication...");

            var clientId = !string.IsNullOrWhiteSpace(CustomClientId)
                ? CustomClientId.Trim()
                : null;
            var clientSecret = !string.IsNullOrWhiteSpace(CustomClientSecret)
                ? CustomClientSecret.Trim()
                : null;

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                _logger.LogWarning("Google Drive credentials not configured. Client ID and Client Secret are required.");
                return OperationResult<bool>.CreateFailure(
                    "Google Drive is not configured. Enter your Client ID and Client Secret in Publisher Studio. " +
                    "Google requires creating a Google Cloud Project, completing 'Project configuration' under Google Auth Platform to create an App, " +
                    "and creating an OAuth Client ID with Application type set to 'Desktop app'.");
            }

            var clientSecrets = new ClientSecrets
            {
                ClientId = clientId,
                ClientSecret = clientSecret,
            };

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (!cancellationToken.CanBeCanceled)
            {
                linkedCts.CancelAfter(TimeSpan.FromSeconds(90));
            }

            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                clientSecrets,
                Scopes,
                "user",
                linkedCts.Token,
                new FileDataStore("GenHub.GoogleDrive.Tokens", fullPath: false));

            _driveService = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });

            _logger.LogInformation("Successfully authenticated with Google Drive");
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Google Drive authentication was canceled or timed out.");
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
                    "Google OAuth Error (redirect_uri_mismatch): The OAuth Client ID was created as a 'Web application' instead of 'Desktop app'. " +
                    "In Google Cloud Console, create a new OAuth Client ID with Application type set to 'Desktop app' and use its credentials.");
            }

            if (errorMsg.Contains("access_denied", StringComparison.OrdinalIgnoreCase) ||
                errorMsg.Contains("unauthorized_client", StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult<bool>.CreateFailure(
                    "Google OAuth Error (Access Denied): In your Google Cloud Project Configuration, make sure your Google account email " +
                    "is added under Audience > Test users, and verify that your OAuth Client type is set to 'Desktop app'.");
            }

            return OperationResult<bool>.CreateFailure($"Authentication failed: {ex.Message}");
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

            var result = await listRequest.ExecuteAsync(cancellationToken);

            if (result.Files.Count > 0)
            {
                var existingFolderId = result.Files[0].Id;
                _logger.LogInformation("Found existing publisher folder: {FolderId}", existingFolderId);
                return OperationResult<string>.CreateSuccess(existingFolderId);
            }

            // Create new folder
            var folderMetadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = PublisherFolderName,
                MimeType = "application/vnd.google-apps.folder",
            };

            var createRequest = _driveService.Files.Create(folderMetadata);
            createRequest.Fields = "id, name";
            var createdFolder = await createRequest.ExecuteAsync(cancellationToken);

            // Make the folder publicly viewable so direct download links work
            await MakePublicAsync(createdFolder.Id, cancellationToken);

            _logger.LogInformation("Created publisher folder: {FolderId}", createdFolder.Id);
            return OperationResult<string>.CreateSuccess(createdFolder.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get or create publisher folder");
            return OperationResult<string>.CreateFailure($"Folder operation failed: {ex.Message}");
        }
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
            // Get or create parent folder
            string parentFolderId;
            if (string.IsNullOrEmpty(folderPath))
            {
                var folderResult = await GetOrCreatePublisherFolderAsync(cancellationToken);
                if (!folderResult.Success)
                {
                    return OperationResult<HostingUploadResult>.CreateFailure(folderResult);
                }

                parentFolderId = folderResult.Data!;
            }
            else
            {
                parentFolderId = folderPath;
            }

            var fileMetadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = fileName,
                Parents = [parentFolderId],
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
                var error = uploadResult.Exception?.Message ?? "Unknown upload error";
                _logger.LogError(uploadResult.Exception, "Upload failed for {FileName}", fileName);
                return OperationResult<HostingUploadResult>.CreateFailure($"Upload failed: {error}");
            }

            var uploadedFile = uploadRequest.ResponseBody;

            // Make file publicly readable
            await MakePublicAsync(uploadedFile.Id, cancellationToken);

            var directDownloadUrl = string.Format(HostingConstants.GoogleDriveDownloadUrlTemplate, uploadedFile.Id);

            var result = new HostingUploadResult
            {
                PublicUrl = uploadedFile.WebViewLink ?? directDownloadUrl,
                DirectDownloadUrl = directDownloadUrl,
                FileId = uploadedFile.Id,
                FileSize = uploadedFile.Size ?? fileStream.Length,
            };

            _logger.LogInformation("Uploaded file to Google Drive: {FileName} ({FileId})", fileName, uploadedFile.Id);
            return OperationResult<HostingUploadResult>.CreateSuccess(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file to Google Drive");
            return OperationResult<HostingUploadResult>.CreateFailure($"Upload failed: {ex.Message}");
        }
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
                var error = uploadResult.Exception?.Message ?? "Unknown update error";
                _logger.LogError(uploadResult.Exception, "Update failed for {FileId}", fileId);
                return OperationResult<HostingUploadResult>.CreateFailure($"Update failed: {error}");
            }

            var updatedFile = updateRequest.ResponseBody;
            var directDownloadUrl = string.Format(HostingConstants.GoogleDriveDownloadUrlTemplate, updatedFile.Id);

            var result = new HostingUploadResult
            {
                PublicUrl = updatedFile.WebViewLink ?? directDownloadUrl,
                DirectDownloadUrl = directDownloadUrl,
                FileId = updatedFile.Id,
                FileSize = updatedFile.Size ?? fileStream.Length,
            };

            _logger.LogInformation("Updated file on Google Drive: {FileName} ({FileId})", fileName, updatedFile.Id);
            return OperationResult<HostingUploadResult>.CreateSuccess(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update file on Google Drive");
            return OperationResult<HostingUploadResult>.CreateFailure($"Update failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<HostingUploadResult>> UploadCatalogAsync(
        string catalogJson,
        string publisherId,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(catalogJson);
        using var stream = new MemoryStream(bytes);
        var fileName = $"catalog-{publisherId}.json";
        return await UploadFileAsync(stream, fileName, null, progress, cancellationToken);
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
            // Find the GenHub_Publisher folder
            var folderResult = await GetOrCreatePublisherFolderAsync(cancellationToken);
            if (!folderResult.Success || string.IsNullOrEmpty(folderResult.Data))
            {
                return OperationResult<HostingState?>.CreateSuccess(null);
            }

            var folderId = folderResult.Data;

            // List files in the folder
            var listRequest = _driveService.Files.List();
            listRequest.Q = $"'{folderId}' in parents and trashed = false";
            listRequest.Fields = "files(id, name, size, modifiedTime)";

            var fileList = await listRequest.ExecuteAsync(cancellationToken);

            if (fileList.Files.Count == 0)
            {
                return OperationResult<HostingState?>.CreateSuccess(null);
            }

            var state = new HostingState
            {
                ProviderId = HostingConstants.GoogleDrive,
                FolderUrl = $"https://drive.google.com/drive/folders/{folderId}",
                LastPublished = DateTime.UtcNow,
            };

            foreach (var file in fileList.Files)
            {
                var directUrl = string.Format(HostingConstants.GoogleDriveDownloadUrlTemplate, file.Id);
                var size = file.Size ?? 0L;
                var modified = file.ModifiedTimeDateTimeOffset?.UtcDateTime ?? DateTime.UtcNow;

                if (file.Name == HostingConstants.DefaultDefinitionFileName)
                {
                    state.Definition = new HostedFileInfo
                    {
                        FileId = file.Id,
                        Url = directUrl,
                        FileSize = size,
                        LastUpdated = modified,
                    };
                }
                else if (file.Name.StartsWith("catalog-", StringComparison.OrdinalIgnoreCase) &&
                         file.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    var catalogId = file.Name["catalog-".Length..^".json".Length];
                    state.Catalogs.Add(new CatalogHostingInfo
                    {
                        CatalogId = catalogId,
                        CatalogName = catalogId,
                        FileName = file.Name,
                        FileId = file.Id,
                        Url = directUrl,
                        FileSize = size,
                        LastUpdated = modified,
                    });
                }
                else
                {
                    state.Artifacts.Add(new ArtifactHostingInfo
                    {
                        FileName = file.Name,
                        FileId = file.Id,
                        Url = directUrl,
                        FileSize = size,
                        LastUpdated = modified,
                    });
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
            _logger.LogError(ex, "Failed to recover hosting state from Google Drive");
            return OperationResult<HostingState?>.CreateFailure($"Recovery failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public string GetSubscriptionLink(string catalogUrl)
    {
        return $"genhub://subscribe?url={Uri.EscapeDataString(catalogUrl)}";
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

    private string GetMimeType(string fileName)
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

    private string? ExtractFileId(string url)
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
}
