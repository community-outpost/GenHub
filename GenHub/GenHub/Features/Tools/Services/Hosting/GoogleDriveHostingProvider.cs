using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;
using GenHub.Features.Tools.Interfaces;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using GenHub.Core.Constants;

namespace GenHub.Features.Tools.Services.Hosting;

/// <summary>
/// Hosting provider for Google Drive.
/// Enables publishers to host catalogs and artifacts on their personal Google Drive.
/// </summary>
/// <remarks>
/// Google Drive is the recommended hosting option because:
/// - Free storage (15GB shared with Gmail/Photos)
/// - Stable URLs when updating files in-place
/// - OAuth flow for secure authentication
/// - No technical setup required (unlike GitHub Pages)
/// </remarks>
public class GoogleDriveHostingProvider : IHostingProvider
{
    private const string ApplicationName = "GenHub Publisher Studio";
    private const string PublisherFolderName = "GenHub_Publisher";
    private static readonly string[] Scopes = { DriveService.Scope.DriveFile };

    private readonly ILogger<GoogleDriveHostingProvider> _logger;
    private readonly IConfiguration _configuration;
    private DriveService? _driveService;
    private string? _publisherFolderId;

    /// <summary>
    /// Initializes a new instance of the <see cref="GoogleDriveHostingProvider"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="configuration">The configuration.</param>
    public GoogleDriveHostingProvider(ILogger<GoogleDriveHostingProvider> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <inheritdoc />
    public string ProviderId => "google_drive";

    /// <inheritdoc />
    public string DisplayName => "Google Drive";

    /// <inheritdoc />
    public string Description => "Host your catalogs and artifacts on Google Drive. Free, reliable, and easy to set up.";

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

            // Check for credentials from environment or configuration
            var clientId = Environment.GetEnvironmentVariable("GENHUB_GOOGLE_CLIENT_ID")
                        ?? _configuration[ConfigurationKeys.GoogleDriveClientId];
            var clientSecret = Environment.GetEnvironmentVariable("GENHUB_GOOGLE_CLIENT_SECRET")
                        ?? _configuration[ConfigurationKeys.GoogleDriveClientSecret];

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                _logger.LogWarning("Google Drive credentials not configured. Please set GENHUB_GOOGLE_CLIENT_ID and GENHUB_GOOGLE_CLIENT_SECRET environment variables or update appsettings.json.");
                return OperationResult<bool>.CreateFailure(
                    "Google Drive is not configured. Please set the required credentials in environment variables or appsettings.json.");
            }

            var clientSecrets = new ClientSecrets
            {
                ClientId = clientId,
                ClientSecret = clientSecret
            };

            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                clientSecrets,
                Scopes,
                "user",
                cancellationToken,
                new FileDataStore("GenHub.GoogleDrive.Tokens", fullPath: false));

            _driveService = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName
            });

            _logger.LogInformation("Successfully authenticated with Google Drive");
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to authenticate with Google Drive");
            return OperationResult<bool>.CreateFailure($"Authentication failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task SignOutAsync()
    {
        _driveService?.Dispose();
        _driveService = null;
        _publisherFolderId = null;
        _logger.LogInformation("Signed out from Google Drive");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<OperationResult<string>> GetOrCreatePublisherFolderAsync(CancellationToken cancellationToken = default)
    {
        if (_driveService == null)
        {
            return OperationResult<string>.CreateFailure("Not authenticated with Google Drive");
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
                _publisherFolderId = result.Files[0].Id;
                _logger.LogInformation("Found existing publisher folder: {FolderId}", _publisherFolderId);
                return OperationResult<string>.CreateSuccess(_publisherFolderId);
            }

            // Create new folder
            var folderMetadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = PublisherFolderName,
                MimeType = "application/vnd.google-apps.folder"
            };

            var createRequest = _driveService.Files.Create(folderMetadata);
            createRequest.Fields = "id";

            var folder = await createRequest.ExecuteAsync(cancellationToken);
            _publisherFolderId = folder.Id;

            _logger.LogInformation("Created new publisher folder: {FolderId}", _publisherFolderId);
            return OperationResult<string>.CreateSuccess(_publisherFolderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get or create publisher folder");
            return OperationResult<string>.CreateFailure($"Failed to get/create folder: {ex.Message}");
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
            return OperationResult<HostingUploadResult>.CreateFailure("Not authenticated with Google Drive");
        }

        try
        {
            // Ensure publisher folder exists
            var folderResult = await GetOrCreatePublisherFolderAsync(cancellationToken);
            if (!folderResult.Success)
            {
                return OperationResult<HostingUploadResult>.CreateFailure(folderResult);
            }

            progress?.Report(10);

            var fileMetadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = fileName,
                Parents = new List<string> { _publisherFolderId! }
            };

            var request = _driveService.Files.Create(fileMetadata, fileStream, "application/octet-stream");
            request.Fields = "id, webContentLink, size";

            progress?.Report(30);

            var uploadProgress = await request.UploadAsync(cancellationToken);
            if (uploadProgress.Status != Google.Apis.Upload.UploadStatus.Completed)
            {
                return OperationResult<HostingUploadResult>.CreateFailure($"Upload failed: {uploadProgress.Exception?.Message}");
            }

            var file = request.ResponseBody;

            progress?.Report(80);

            // Make file publicly accessible
            await MakeFilePublicAsync(file.Id, cancellationToken);

            progress?.Report(100);

            var downloadUrl = $"https://drive.google.com/uc?export=download&id={file.Id}";

            var result = new HostingUploadResult
            {
                PublicUrl = $"https://drive.google.com/file/d/{file.Id}/view",
                DirectDownloadUrl = downloadUrl,
                FileId = file.Id,
                FileSize = file.Size ?? 0
            };

            _logger.LogInformation("Uploaded file {FileName} to Google Drive: {FileId}", fileName, file.Id);
            return OperationResult<HostingUploadResult>.CreateSuccess(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file {FileName}", fileName);
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
            return OperationResult<HostingUploadResult>.CreateFailure("Not authenticated with Google Drive");
        }

        try
        {
            progress?.Report(10);

            var fileMetadata = new Google.Apis.Drive.v3.Data.File();
            var request = _driveService.Files.Update(fileMetadata, fileId, fileStream, "application/octet-stream");
            request.Fields = "id, size";

            progress?.Report(30);

            var uploadProgress = await request.UploadAsync(cancellationToken);
            if (uploadProgress.Status != Google.Apis.Upload.UploadStatus.Completed)
            {
                return OperationResult<HostingUploadResult>.CreateFailure($"Update failed: {uploadProgress.Exception?.Message}");
            }

            var file = request.ResponseBody;

            progress?.Report(100);

            var downloadUrl = $"https://drive.google.com/uc?export=download&id={file.Id}";

            var result = new HostingUploadResult
            {
                PublicUrl = $"https://drive.google.com/file/d/{file.Id}/view",
                DirectDownloadUrl = downloadUrl,
                FileId = file.Id,
                FileSize = file.Size ?? 0
            };

            _logger.LogInformation("Updated file {FileName} on Google Drive: {FileId}", fileName, file.Id);
            return OperationResult<HostingUploadResult>.CreateSuccess(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update file {FileId}", fileId);
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
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(catalogJson));
        var fileName = $"catalog-{publisherId}.json";
        return await UploadFileAsync(stream, fileName, null, progress, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<HostingState?>> RecoverHostingStateAsync(CancellationToken cancellationToken = default)
    {
        if (_driveService == null)
        {
            return OperationResult<HostingState?>.CreateFailure("Not authenticated with Google Drive");
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
            _publisherFolderId = folderId;

            // Find all files in the folder
            var filesRequest = _driveService.Files.List();
            filesRequest.Q = $"'{folderId}' in parents and trashed = false";
            filesRequest.Fields = "files(id, name, size, modifiedTime)";

            var filesResult = await filesRequest.ExecuteAsync(cancellationToken);

            var state = new HostingState
            {
                ProviderId = ProviderId,
                FolderId = folderId,
                FolderUrl = $"https://drive.google.com/drive/folders/{folderId}"
            };

            foreach (var file in filesResult.Files)
            {
                var downloadUrl = $"https://drive.google.com/uc?export=download&id={file.Id}";
                var lastUpdated = file.ModifiedTimeDateTimeOffset?.DateTime ?? DateTime.UtcNow;

                if (file.Name == "publisher.json")
                {
                    state.Definition = new HostedFileInfo
                    {
                        FileId = file.Id,
                        Url = downloadUrl,
                        LastUpdated = lastUpdated
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
            return $"https://drive.google.com/uc?export=download&id={fileId}";
        }

        return shareUrl;
    }

    private async Task MakeFilePublicAsync(string fileId, CancellationToken cancellationToken)
    {
        var permission = new Permission
        {
            Type = "anyone",
            Role = "reader",
        };

        await _driveService!.Permissions.Create(permission, fileId).ExecuteAsync(cancellationToken);
    }
}
