using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Services;
using GenHub.Core.Interfaces.Telemetry;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.UploadThing;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.Services;

/// <summary>
/// Service for uploading and deleting files via the GenHub upload gateway proxy.
/// </summary>
public sealed class UploadThingService(
    HttpClient httpClient,
    ILogger<UploadThingService> logger,
    ITelemetryService? telemetryService = null) : IUploadThingService
{
    /// <inheritdoc />
    public async Task<OperationResult<UploadResult>> UploadFileAsync(
        string filePath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
        {
            logger.LogError("File to upload does not exist: {Path}", filePath);
            return OperationResult<UploadResult>.CreateFailure($"File not found: {filePath}");
        }

        var rawFileName = Path.GetFileName(filePath);
        var stopwatch = Stopwatch.StartNew();
        long fileLength = 0;

        try
        {
            var fileName = PathHelper.SanitizeFileName(rawFileName);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = ApiConstants.DefaultUploadFileName;
            }

            fileLength = new FileInfo(filePath).Length;
            var streamProgress = progress != null ? new Progress<double>(p => progress.Report(p * 0.85)) : null;
            await using var fileStream = File.OpenRead(filePath);
            using var fileContent = new ProgressableStreamContent(fileStream, fileLength, streamProgress);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(ApiConstants.MediaTypeZip);

            using var formContent = new MultipartFormDataContent();
            formContent.Add(fileContent, ApiConstants.UploadMultipartFileFieldName, fileName);

            // The gateway only recognizes quoted disposition values. .NET emits name and
            // filename unquoted by default, which makes the gateway misclassify the file part
            // and corrupt binary uploads, so quote both explicitly like curl does. The override
            // is ASCII-only: serializing raw Unicode through Parse degrades to Latin-1 and
            // mangles non-ASCII filenames, while the runtime default MIME-encodes them losslessly.
            if (Ascii.IsValid(fileName))
            {
                fileContent.Headers.ContentDisposition = ContentDispositionHeaderValue.Parse(
                    $"form-data; name=\"{ApiConstants.UploadMultipartFileFieldName}\"; filename=\"{ToHeaderSafeFileName(fileName)}\"");
            }

            progress?.Report(0.88);
            using var response = await httpClient.PostAsync(ApiConstants.DefaultUploadUrl, formContent, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("Upload failed with status {Status}: {Error}", response.StatusCode, errorBody);
                var message = !string.IsNullOrWhiteSpace(errorBody)
                    ? $"Upload rejected ({response.StatusCode}): {errorBody}"
                    : $"Upload failed with status {response.StatusCode}";
                telemetryService?.TrackEvent(TelemetryConstants.Events.UploadThingUploadFailed, new Dictionary<string, object?>
                {
                    [TelemetryConstants.Properties.FileName] = rawFileName,
                    [TelemetryConstants.Properties.SizeMb] = Math.Round(fileLength / (1024.0 * 1024.0), 2),
                    [TelemetryConstants.Properties.DurationSeconds] = stopwatch.Elapsed.TotalSeconds,
                    [TelemetryConstants.Properties.ErrorMessage] = message,
                });
                return OperationResult<UploadResult>.CreateFailure(message);
            }

            var result = await response.Content.ReadFromJsonAsync<DirectUploadResponse>(cancellationToken: ct);
            if (result?.PublicUrl == null || result.FileKey == null || result.DeleteToken == null)
            {
                logger.LogError("Gateway returned incomplete upload response.");
                return OperationResult<UploadResult>.CreateFailure("Gateway returned incomplete upload response.");
            }

            progress?.Report(1.0);
            logger.LogInformation("File uploaded successfully to {Url}", result.PublicUrl);

            telemetryService?.TrackEvent(TelemetryConstants.Events.UploadThingUploadCompleted, new Dictionary<string, object?>
            {
                [TelemetryConstants.Properties.FileName] = rawFileName,
                [TelemetryConstants.Properties.SizeMb] = Math.Round(fileLength / (1024.0 * 1024.0), 2),
                [TelemetryConstants.Properties.DurationSeconds] = stopwatch.Elapsed.TotalSeconds,
                [TelemetryConstants.Properties.FileSizeBytes] = fileLength,
            });

            return OperationResult<UploadResult>.CreateSuccess(new UploadResult(result.PublicUrl, result.FileKey, result.DeleteToken));
        }
        catch (Exception ex) when ((ex is HttpRequestException or IOException or UnauthorizedAccessException or JsonException or FormatException or InvalidOperationException) && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Exception occurred during file upload");
            telemetryService?.TrackEvent(TelemetryConstants.Events.UploadThingUploadFailed, new Dictionary<string, object?>
            {
                [TelemetryConstants.Properties.FileName] = rawFileName,
                [TelemetryConstants.Properties.DurationSeconds] = stopwatch.Elapsed.TotalSeconds,
                [TelemetryConstants.Properties.ErrorMessage] = ex.Message,
            });
            return OperationResult<UploadResult>.CreateFailure($"Upload error: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> DeleteFileAsync(string fileKey, string deleteToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fileKey) || string.IsNullOrWhiteSpace(deleteToken))
        {
            logger.LogWarning("Cannot delete file: fileKey or deleteToken is missing.");
            return OperationResult<bool>.CreateFailure("Missing fileKey or deleteToken.");
        }

        try
        {
            var deleteRequest = new DeleteUploadRequest(fileKey, deleteToken);
            using var response = await httpClient.PostAsJsonAsync(ApiConstants.DefaultUploadDeleteUrl, deleteRequest, ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("Delete request rejected with status {Status}: {Error}", response.StatusCode, error);
                return OperationResult<bool>.CreateFailure($"Delete failed with status {response.StatusCode}: {error}");
            }

            var result = await response.Content.ReadFromJsonAsync<DeleteUploadResponse>(cancellationToken: ct);
            var isSuccess = result?.Success ?? response.IsSuccessStatusCode;

            if (isSuccess)
            {
                logger.LogInformation("File {Key} deleted successfully from cloud storage.", fileKey);
                return OperationResult<bool>.CreateSuccess(true);
            }

            return OperationResult<bool>.CreateFailure("Cloud storage reported deletion failure.");
        }
        catch (Exception ex) when ((ex is HttpRequestException or JsonException or InvalidOperationException) && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Exception occurred while deleting file {Key}", fileKey);
            return OperationResult<bool>.CreateFailure($"Deletion error: {ex.Message}");
        }
    }

    private static string ToHeaderSafeFileName(string fileName)
    {
        return fileName.Replace("\"", string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
    }
}
