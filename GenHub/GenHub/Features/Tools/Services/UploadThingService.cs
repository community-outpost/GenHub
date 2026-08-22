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
            var fileInfo = new FileInfo(filePath);
            var fileName = Path.GetFileName(filePath);

            // Step 1: Request presigned upload URL and HMAC deletion token from the gateway
            var prepareRequest = new PrepareUploadRequest(fileName, fileInfo.Length, ApiConstants.MediaTypeZip);
            using var prepareResponse = await httpClient.PostAsJsonAsync(ApiConstants.DefaultUploadPrepareUrl, prepareRequest, ct);

            if (!prepareResponse.IsSuccessStatusCode)
            {
                var errorBody = await prepareResponse.Content.ReadAsStringAsync(ct);
                logger.LogError("Upload preparation failed with status {Status}: {Error}", prepareResponse.StatusCode, errorBody);
                return null;
            }

            var instruction = await prepareResponse.Content.ReadFromJsonAsync<PrepareUploadResponse>(cancellationToken: ct);
            if (instruction?.UploadUrl == null || instruction.FileKey == null || instruction.DeleteToken == null)
            {
                logger.LogError("Gateway returned incomplete upload instructions.");
                return null;
            }

            progress?.Report(0.2);

            // Step 2: Stream the binary directly to UploadThing / S3 presigned URL
            await using var fileStream = File.OpenRead(filePath);
            using var content = new StreamContent(fileStream);
            content.Headers.ContentType = new MediaTypeHeaderValue(ApiConstants.MediaTypeZip);

            using var uploadRequest = new HttpRequestMessage(HttpMethod.Put, instruction.UploadUrl)
            {
                Content = content,
            };

            using var uploadResponse = await httpClient.SendAsync(uploadRequest, ct);
            if (!uploadResponse.IsSuccessStatusCode)
            {
                var uploadError = await uploadResponse.Content.ReadAsStringAsync(ct);
                logger.LogError("Direct upload to storage provider failed with status {Status}: {Error}", uploadResponse.StatusCode, uploadError);
                return null;
            }

            progress?.Report(1.0);

            var publicUrl = instruction.PublicUrl ?? string.Format(ApiConstants.UploadThingPublicUrlFormat, instruction.FileKey);
            logger.LogInformation("File uploaded successfully to {Url}", publicUrl);

            return new UploadResult(publicUrl, instruction.FileKey, instruction.DeleteToken);
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
