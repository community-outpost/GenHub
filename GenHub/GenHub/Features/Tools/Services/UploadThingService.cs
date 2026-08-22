using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Services;
using GenHub.Core.Models.Tools.UploadThing;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.Services;

/// <summary>
/// Service for uploading and deleting files via the GenHub upload gateway proxy.
/// </summary>
public sealed class UploadThingService(
    HttpClient httpClient,
    ILogger<UploadThingService> logger) : IUploadThingService
{
    /// <inheritdoc />
    public async Task<UploadResult?> UploadFileAsync(
        string filePath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
        {
            logger.LogError("File to upload does not exist: {Path}", filePath);
            return null;
        }

        try
        {
            var fileName = Path.GetFileName(filePath);

            progress?.Report(0.1);

            await using var fileStream = File.OpenRead(filePath);
            using var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(ApiConstants.MediaTypeZip);

            using var formContent = new MultipartFormDataContent();
            formContent.Add(fileContent, "file", fileName);

            using var response = await httpClient.PostAsync(ApiConstants.DefaultUploadUrl, formContent, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("Upload failed with status {Status}: {Error}", response.StatusCode, errorBody);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<DirectUploadResponse>(cancellationToken: ct);
            if (result?.PublicUrl == null || result.FileKey == null || result.DeleteToken == null)
            {
                logger.LogError("Gateway returned incomplete upload response.");
                return null;
            }

            progress?.Report(1.0);
            logger.LogInformation("File uploaded successfully to {Url}", result.PublicUrl);

            return new UploadResult(result.PublicUrl, result.FileKey, result.DeleteToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception occurred during file upload");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteFileAsync(string fileKey, string deleteToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fileKey) || string.IsNullOrWhiteSpace(deleteToken))
        {
            logger.LogWarning("Cannot delete file: fileKey or deleteToken is missing.");
            return false;
        }

        try
        {
            var deleteRequest = new DeleteUploadRequest(fileKey, deleteToken);
            using var response = await httpClient.PostAsJsonAsync(ApiConstants.DefaultUploadDeleteUrl, deleteRequest, ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("Delete request rejected with status {Status}: {Error}", response.StatusCode, error);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<DeleteUploadResponse>(cancellationToken: ct);
            var isSuccess = result?.Success ?? response.IsSuccessStatusCode;

            if (isSuccess)
            {
                logger.LogInformation("File {Key} deleted successfully from cloud storage.", fileKey);
            }

            return isSuccess;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception occurred while deleting file {Key}", fileKey);
            return false;
        }
    }
}
