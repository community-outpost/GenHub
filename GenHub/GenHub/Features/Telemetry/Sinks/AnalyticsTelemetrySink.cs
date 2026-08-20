using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Telemetry;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Telemetry;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Telemetry.Sinks;

/// <summary>
/// Telemetry sink for delivering anonymous usage, game session, and update metrics to an analytics endpoint such as PostHog.
/// </summary>
public sealed class AnalyticsTelemetrySink(
    ILogger<AnalyticsTelemetrySink> logger,
    HttpClient? httpClient = null) : ITelemetrySink
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private const int MaxBufferSize = 100;
    private readonly ConcurrentQueue<TelemetryEvent> _buffer = new();
    private string? _endpointUrl = Environment.GetEnvironmentVariable("POSTHOG_CAPTURE_URL") ?? (Environment.GetEnvironmentVariable("POSTHOG_HOST") != null ? $"{Environment.GetEnvironmentVariable("POSTHOG_HOST")?.TrimEnd('/')}/capture/" : TelemetryConstants.DefaultPostHogCaptureEndpoint);
    private string? _apiKey = Environment.GetEnvironmentVariable("POSTHOG_API_KEY") ?? Environment.GetEnvironmentVariable("GENHUB_POSTHOG_API_KEY") ?? TelemetryConstants.DefaultPostHogApiKey;

    /// <inheritdoc/>
    public string Name => "Analytics";

    /// <summary>
    /// Gets or sets the remote HTTP endpoint URL for analytics ingestion (e.g. PostHog capture endpoint).
    /// When null or empty, defaults to the configured default PostHog capture URL or buffers locally.
    /// </summary>
    public string? EndpointUrl
    {
        get => _endpointUrl;
        set => _endpointUrl = value;
    }

    /// <summary>
    /// Gets or sets the analytics project API token / key (e.g. PostHog project token).
    /// </summary>
    public string? ApiKey
    {
        get => _apiKey;
        set => _apiKey = value;
    }

    /// <inheritdoc/>
    public bool CanHandle(TelemetryEvent telemetryEvent)
    {
        ArgumentNullException.ThrowIfNull(telemetryEvent);
        return telemetryEvent.Level == TelemetryLevel.AnonymousMetrics;
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> EmitAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(telemetryEvent);

        if (!CanHandle(telemetryEvent))
        {
            return OperationResult<bool>.CreateSuccess(false);
        }

        var endpoint = EndpointUrl;
        var apiKey = ApiKey;

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey) || httpClient == null)
        {
            // Offline / unconfigured remote endpoint mode: buffer in memory
            EnqueueBounded(telemetryEvent);
            return OperationResult<bool>.CreateSuccess(true);
        }

        try
        {
            var postHogProperties = new Dictionary<string, object?>(telemetryEvent.Properties ?? new Dictionary<string, object?>())
            {
                ["$lib"] = TelemetryConstants.AppName,
                ["$app_version"] = telemetryEvent.AppVersion,
                ["$os"] = telemetryEvent.Platform,
                ["$process_person_profile"] = false,
            };

            if (!string.IsNullOrEmpty(telemetryEvent.SessionId))
            {
                postHogProperties["$session_id"] = telemetryEvent.SessionId;
            }

            var payload = new Dictionary<string, object?>
            {
                ["api_key"] = apiKey,
                ["event"] = telemetryEvent.EventName,
                ["distinct_id"] = telemetryEvent.InstallationId,
                ["properties"] = postHogProperties,
                ["timestamp"] = telemetryEvent.Timestamp.ToString("o"),
            };

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await httpClient.PostAsync(endpoint, content, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return OperationResult<bool>.CreateSuccess(true);
            }

            logger.LogDebug("[Analytics] Endpoint returned status code {StatusCode}", response.StatusCode);
            EnqueueBounded(telemetryEvent);
            return OperationResult<bool>.CreateFailure($"Remote endpoint returned {response.StatusCode}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[Analytics] Failed to send telemetry event to endpoint");
            EnqueueBounded(telemetryEvent);
            return OperationResult<bool>.CreateFailure(ex.Message);
        }
    }

    private void EnqueueBounded(TelemetryEvent telemetryEvent)
    {
        _buffer.Enqueue(telemetryEvent);
        while (_buffer.Count > MaxBufferSize)
        {
            _buffer.TryDequeue(out _);
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> FlushAsync(CancellationToken cancellationToken = default)
    {
        if (httpClient == null || _buffer.IsEmpty)
        {
            return OperationResult<bool>.CreateSuccess(true);
        }

        var failed = false;
        var count = _buffer.Count;
        for (var i = 0; i < count && _buffer.TryDequeue(out var ev); i++)
        {
            var res = await EmitAsync(ev, cancellationToken);
            if (!res.Success)
            {
                failed = true;
            }
        }

        return failed
            ? OperationResult<bool>.CreateFailure("Failed to flush some buffered events")
            : OperationResult<bool>.CreateSuccess(true);
    }
}
