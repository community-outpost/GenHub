using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.Services;

/// <summary>
/// Implementation of <see cref="IUploadThingService"/> for uploading files to UploadThing cloud storage using V7 API.
/// </summary>
public sealed class UploadThingService(
    HttpClient httpClient,
    ILogger<UploadThingService> logger) : IUploadThingService
{
    /// <inheritdoc />
    public async Task<string?> UploadFileAsync(
        string filePath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var token = Environment.GetEnvironmentVariable(ApiConstants.UploadThingTokenEnvVar) ??
                    Environment.GetEnvironmentVariable(ApiConstants.UploadThingTokenEnvVarAlt);

        // Fallback to build-time injected token if no env var is found
        if (string.IsNullOrEmpty(token))
        {
            token = ApiConstants.BuildTimeUploadThingToken;
        }

        if (string.IsNullOrEmpty(token))
        {
            logger.LogError("UploadThing V7 Token is missing. Ensure UPLOADTHING_TOKEN is set.");
            return null;
        }

        if (!File.Exists(filePath))
        {
            logger.LogError("File to upload does not exist: {Path}", filePath);
            return null;
        }

        logger.LogInformation("Uploading to UploadThing V7: {Path}", filePath);

        try
        {
            var fileInfo = new FileInfo(filePath);
            var fileName = Path.GetFileName(filePath);

            // Step 1: Prepare the upload
            var requestPayload = new V7FileRequestDetail
            {
                FileName = fileName,
                FileSize = fileInfo.Length,
                ContentTypes = [ApiConstants.MediaTypeZip],
            };

            var prepareRequest = new HttpRequestMessage(HttpMethod.Post, ApiConstants.UploadThingPrepareUrl);
            prepareRequest.Headers.Add(ApiConstants.UploadThingApiKeyHeader, token);
            prepareRequest.Headers.Add(ApiConstants.UploadThingVersionHeader, ApiConstants.UploadThingApiVersion);
            prepareRequest.Content = JsonContent.Create(requestPayload);

            var prepareResponse = await httpClient.SendAsync(prepareRequest, ct);
            if (!prepareResponse.IsSuccessStatusCode)
            {
                var error = await prepareResponse.Content.ReadAsStringAsync(ct);
                logger.LogError("V7 PrepareUpload failed: {Status} - {Error}", prepareResponse.StatusCode, error);
                return null;
            }

            var instruction = await prepareResponse.Content.ReadFromJsonAsync<V7FileInstruction>(cancellationToken: ct);

            if (instruction?.PresignedUrl == null || instruction?.Key == null)
            {
                var rawResponse = await prepareResponse.Content.ReadAsStringAsync(ct);
                logger.LogError("UploadThing V7 returned 200 OK but missing required fields. Response: {Response}", rawResponse);
                return null;
            }

            // Step 2: Upload binary via PUT with multipart/form-data
            using var fileStream = File.OpenRead(filePath);
            var multipartContent = new MultipartFormDataContent();
            var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(ApiConstants.MediaTypeZip);
            multipartContent.Add(fileContent, "file", fileName);

            var uploadRequest = new HttpRequestMessage(HttpMethod.Put, instruction.PresignedUrl)
            {
                Content = multipartContent,
            };
            uploadRequest.Headers.UserAgent.ParseAdd(ApiConstants.DefaultUserAgent);

            progress?.Report(0.6);

            var uploadResponse = await httpClient.SendAsync(uploadRequest, ct);
            if (!uploadResponse.IsSuccessStatusCode)
            {
                var uploadError = await uploadResponse.Content.ReadAsStringAsync(ct);
                logger.LogError("V7 PUT Binary Upload failed: {Status} - {Error}", uploadResponse.StatusCode, uploadError);
                return null;
            }

            var publicFileUrl = string.Format(ApiConstants.UploadThingPublicUrlFormat, instruction.Key);

            logger.LogInformation("UploadThing V7 successful. Public URL: {Url}", publicFileUrl);
            progress?.Report(1.0);

            return publicFileUrl;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception in UploadThing V7 flow");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteFileAsync(string fileKey, CancellationToken ct = default)
    {
        var token = Environment.GetEnvironmentVariable(ApiConstants.UploadThingTokenEnvVar) ??
                    Environment.GetEnvironmentVariable(ApiConstants.UploadThingTokenEnvVarAlt);

        // Fallback to build-time injected token if no env var is found
        if (string.IsNullOrEmpty(token))
        {
            token = ApiConstants.BuildTimeUploadThingToken;
        }

        if (string.IsNullOrEmpty(token))
        {
            logger.LogError("UploadThing Token is missing.");
            return false;
        }

        try
        {
            var requestPayload = new V6DeleteRequest { FileKeys = [fileKey] };
            var request = new HttpRequestMessage(HttpMethod.Post, ApiConstants.UploadThingDeleteUrl);
            request.Headers.Add(ApiConstants.UploadThingApiKeyHeader, token);

            request.Content = JsonContent.Create(requestPayload);

            var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("UploadThing Delete failed: {Status} - {Error}", response.StatusCode, error);
                return false;
            }

            logger.LogInformation("Deleted file from UploadThing: {Key}", fileKey);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception deleting file from UploadThing");
            return false;
        }
    }

    // --- V7 Request DTOs ---
    private sealed class V7FileRequestDetail
    {
        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = string.Empty;

        [JsonPropertyName("fileSize")]
        public long FileSize { get; set; }

        [JsonPropertyName("contentTypes")]
        public List<string> ContentTypes { get; set; } = [];
    }

    // --- V7 Response DTO ---
    private sealed class V7FileInstruction
    {
        [JsonPropertyName("url")]
        public string? PresignedUrl { get; set; }

        [JsonPropertyName("key")]
        public string? Key { get; set; }
    }

    // --- V6 Delete DTO ---
    private sealed class V6DeleteRequest
    {
        [JsonPropertyName("fileKeys")]
        public List<string> FileKeys { get; set; } = [];
    }
}