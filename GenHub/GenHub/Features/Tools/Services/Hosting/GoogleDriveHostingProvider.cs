using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;
using GenHub.Features.Tools.Interfaces;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.Services.Hosting;

/// <summary>
/// Hosting provider for Google Drive.
/// Enables publishers to host catalogs and artifacts on their personal Google Drive.
/// </summary>
/// <remarks>
/// Google Drive is the recommended hosting option because:
/// - Free storage (15GB shared with Gmail/Photos).
/// - Stable URLs when updating files in-placeValidateSingleArtifactUrl.
/// - OAuth flow for secure authentication.
/// - No technical setup required (unlike GitHub Pages).
/// </remarks>
public class GoogleDriveHostingProvider : IHostingProvider
{
    private const string ApplicationName = "GenHub Publisher Studio";
    private const string PublisherFolderName = "GenHub_Publisher";
    private static readonly string[] Scopes = { DriveService.Scope.DriveFile };

    private readonly ILogger<GoogleDriveHostingProvider> _logger;
    private DriveService? _driveService;
    private string? _userEmail;

    /// <summary>
    /// Initializes a new instance of the <see cref="GoogleDriveHostingProvider"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public GoogleDriveHostingProvider(ILogger<GoogleDriveHostingProvider> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string ProviderId => "googledrive";

    /// <inheritdoc />
    public string DisplayName => "Google Drive";

    /// <inheritdoc />
    public string Description => "Host your catalogs and artifacts on Google Drive. 15GB free storage with stable download links.";

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

    /// <inheritdoc />
    public async Task<OperationResult<bool>> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting Google Drive authentication...");

            // In production, client secrets should come from configuration
            // or a local credentials.json file
            var secrets = new ClientSecrets
            {
                // Placeholder - in real app, these come from Google Cloud Console
                ClientId = "YOUR_CLIENT_ID.apps.googleusercontent.com",
                ClientSecret = "YOUR_CLIENT_SECRET",
            };

            // Use local file data store for token caching
            var credPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GenHub",
                "google-drive-token");

            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets,
                Scopes,
                "user",
                cancellationToken,
                new FileDataStore(credPath, true));

            _driveService = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });

            // Get user info to verify connection
            var aboutRequest = _driveService.About.Get();
            aboutRequest.Fields = "user";
            var about = await aboutRequest.ExecuteAsync(cancellationToken);
            _userEmail = about.User?.EmailAddress;

            _logger.LogInformation("Authenticated with Google Drive as {Email}", _userEmail);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google Drive authentication failed");
            return OperationResult<bool>.CreateFailure($"Authentication failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Authenticates with an existing DriveService instance (for testing).
    /// </summary>
    /// <param name="driveService">The DriveService instance.</param>
    /// <param name="userEmail">Optional user email.</param>
    public void SetDriveService(DriveService driveService, string? userEmail = null)
    {
        _driveService = driveService;
        _userEmail = userEmail;
    }

    /// <inheritdoc />
    public Task SignOutAsync()
    {
        _driveService = null;
        _userEmail = null;
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
            createRequest.Fields = "id";

            var createdFolder = await createRequest.ExecuteAsync(cancellationToken);
            var folderId = createdFolder.Id;

            // Make the folder publicly readable so files are accessible
            try
            {
                await MakeFilePublicAsync(folderId, cancellationToken);
            }
            catch (GoogleApiException ex)
            {
                _logger.LogWarning(ex, "Google Drive API error setting public permissions on folder {FolderId}", folderId);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Network error setting public permissions on Google Drive folder {FolderId}", folderId);
            }

            _logger.LogInformation("Created new publisher folder: {FolderId}", folderId);
            return OperationResult<string>.CreateSuccess(folderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get or create publisher folder on Google Drive");
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
            // Ensure publisher folder exists
            var folderResult = await GetOrCreatePublisherFolderAsync(cancellationToken);
            if (!folderResult.Success)
            {
                return OperationResult<HostingUploadResult>.CreateFailure(folderResult);
            }

            var folderId = folderResult.Data;
            _logger.LogInformation("Uploading file {FileName} to Google Drive folder {FolderId}", fileName, folderId);

            progress?.Report(10);

            var fileMetadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = fileName,
                Parents = new List<string> { folderId },
            };

            // Determine MIME type
            var mimeType = fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? HostingConstants.JsonContentType
                : HostingConstants.BinaryContentType;

            var request = _driveService.Files.Create(fileMetadata, fileStream, mimeType);
            request.Fields = "id, webViewLink, webContentLink, size";

            progress?.Report(30);

            var uploadProgress = await request.UploadAsync(cancellationToken);
            if (uploadProgress.Status != Google.Apis.Upload.UploadStatus.Completed)
            {
                var error = uploadProgress.Exception?.Message ?? "Unknown upload error";
                _logger.LogError(uploadProgress.Exception, "Google Drive upload failed: {Error}", error);
                return OperationResult<HostingUploadResult>.CreateFailure($"Upload failed: {error}");
            }

            progress?.Report(80);

            var file = request.ResponseBody;

            // Make the file publicly readable
            await MakeFilePublicAsync(file.Id, cancellationToken);

            progress?.Report(100);

            // Direct download link for Google Drive files
            var directDownloadUrl = string.Format(CultureInfo.InvariantCulture, HostingConstants.GoogleDriveDownloadUrlTemplate, file.Id);
            var publicUrl = file.WebViewLink ?? directDownloadUrl;

            var result = new HostingUploadResult
            {
                PublicUrl = publicUrl,
                DirectDownloadUrl = directDownloadUrl,
                FileId = file.Id,
                FileSize = file.Size ?? 0,
            };

            _logger.LogInformation("Uploaded file {FileName} with ID {FileId}", fileName, file.Id);
            return OperationResult<HostingUploadResult>.CreateSuccess(result);
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
            progress?.Report(10);

            var fileMetadata = new Google.Apis.Drive.v3.Data.File();
            var request = _driveService.Files.Update(fileMetadata, fileId, fileStream, HostingConstants.BinaryContentType);
            request.Fields = "id, size";

            progress?.Report(30);

            var uploadProgress = await request.UploadAsync(cancellationToken);
            if (uploadProgress.Status != Google.Apis.Upload.UploadStatus.Completed)
            {
                var error = uploadProgress.Exception?.Message ?? "Unknown update error";
                _logger.LogError(uploadProgress.Exception, "Google Drive update failed: {Error}", error);
                return OperationResult<HostingUploadResult>.CreateFailure($"Update failed: {error}");
            }

            progress?.Report(100);

            var file = request.ResponseBody;
            var directDownloadUrl = string.Format(CultureInfo.InvariantCulture, HostingConstants.GoogleDriveDownloadUrlTemplate, fileId);

            var result = new HostingUploadResult
            {
                PublicUrl = directDownloadUrl,
                DirectDownloadUrl = directDownloadUrl,
                FileId = fileId,
                FileSize = file.Size ?? 0,
            };

            _logger.LogInformation("Updated file {FileId} on Google Drive", fileId);
            return OperationResult<HostingUploadResult>.CreateSuccess(result);
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
        var bytes = Encoding.UTF8.GetBytes(catalogJson);
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
            // Find publisher folder
            var folderRequest = _driveService.Files.List();
            folderRequest.Q = $"name = '{PublisherFolderName}' and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
            folderRequest.Fields = "files(id, name)";

            var folderResult = await folderRequest.ExecuteAsync(cancellationToken);

            if (folderResult.Files.Count == 0)
            {
                _logger.LogInformation("No publisher folder found on Google Drive");
                return OperationResult<HostingState?>.CreateSuccess(null);
            }

            var folderId = folderResult.Files[0].Id;

            // Find all files in the folder
            var filesRequest = _driveService.Files.List();
            filesRequest.Q = $"'{folderId}' in parents and trashed = false";
            filesRequest.Fields = "files(id, name, size, modifiedTime)";

            var filesResult = await filesRequest.ExecuteAsync(cancellationToken);

            var state = new HostingState
            {
                ProviderId = ProviderId,
                FolderId = folderId,
                FolderUrl = $"https://drive.google.com/drive/folders/{folderId}",
            };

            foreach (var file in filesResult.Files)
            {
                var downloadUrl = string.Format(CultureInfo.InvariantCulture, HostingConstants.GoogleDriveDownloadUrlTemplate, file.Id);
                var lastUpdated = file.ModifiedTimeDateTimeOffset?.DateTime ?? DateTime.UtcNow;

                if (file.Name == "publisher.json")
                {
                    state.Definition = new HostedFileInfo
                    {
                        FileId = file.Id,
                        Url = downloadUrl,
                        LastUpdated = lastUpdated,
                    };
                }
                else if (file.Name.StartsWith("catalog-") && file.Name.EndsWith(".json"))
                {
                    var catalogId = file.Name
                        .Replace("catalog-", string.Empty)
                        .Replace(".json", string.Empty);

                    state.Catalogs.Add(new CatalogHostingInfo
                    {
                        FileId = file.Id,
                        Url = downloadUrl,
                        LastUpdated = lastUpdated,
                        CatalogId = catalogId,
                    });
                }
                else if (file.Name.EndsWith(".zip"))
                {
                    // Parse artifact filename: {contentId}-v{version}.zip
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(file.Name);
                    var versionIndex = nameWithoutExt.LastIndexOf("-v", StringComparison.Ordinal);

                    if (versionIndex > 0)
                    {
                        var contentId = nameWithoutExt[..versionIndex];
                        var version = nameWithoutExt[(versionIndex + 2)..];

                        state.Artifacts.Add(new ArtifactHostingInfo
                        {
                            FileId = file.Id,
                            Url = downloadUrl,
                            LastUpdated = lastUpdated,
                            ContentId = contentId,
                            Version = version,
                            FileName = file.Name,
                        });
                    }
                }
            }

            _logger.LogInformation(
                "Recovered hosting state: Definition={HasDef}, Catalogs={CatalogCount}, Artifacts={ArtifactCount}",
                state.Definition != null,
                state.Catalogs.Count,
                state.Artifacts.Count);

            return OperationResult<HostingState?>.CreateSuccess(state);
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

        return url.Contains("drive.google.com", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("docs.google.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public string GetDirectDownloadUrl(string shareUrl)
    {
        // Convert share URL to direct download URL
        // From: https://drive.google.com/file/d/{fileId}/view
        // To: https://drive.google.com/uc?export=download&id={fileId}
        if (shareUrl.Contains("/file/d/"))
        {
            var startIndex = shareUrl.IndexOf("/file/d/", StringComparison.Ordinal) + 8;
            var endIndex = shareUrl.IndexOf("/", startIndex, StringComparison.Ordinal);
            if (endIndex == -1) endIndex = shareUrl.Length;

            var fileId = shareUrl[startIndex..endIndex];
            return string.Format(CultureInfo.InvariantCulture, HostingConstants.GoogleDriveDownloadUrlTemplate, fileId);
        }

        return shareUrl;
    }

    private async Task MakeFilePublicAsync(string fileId, CancellationToken cancellationToken)
    {
        if (_driveService == null)
        {
            return;
        }

        var permission = new Permission
        {
            Type = "anyone",
            Role = "reader",
        };

        await _driveService.Permissions.Create(permission, fileId).ExecuteAsync(cancellationToken);
    }
}
