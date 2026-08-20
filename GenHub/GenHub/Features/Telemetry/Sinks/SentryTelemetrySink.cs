using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices;
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
/// Telemetry sink for delivering unhandled exceptions and crash forensics to Sentry or crash endpoints.
/// </summary>
public sealed class SentryTelemetrySink(
    ILogger<SentryTelemetrySink> logger,
    HttpClient? httpClient = null) : ITelemetrySink
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ConcurrentQueue<TelemetryEvent> _crashBuffer = new();
    private string? _dsnEndpoint;

    /// <inheritdoc/>
    public string Name => "Sentry";

    /// <summary>
    /// Gets or sets the Sentry DSN or HTTP crash reporting endpoint.
    /// When null or empty, defaults to the configured default Sentry DSN or buffers locally.
    /// </summary>
    public string? DsnEndpoint
    {
        get => _dsnEndpoint ?? Environment.GetEnvironmentVariable("SENTRY_DSN") ?? Environment.GetEnvironmentVariable("GENHUB_SENTRY_DSN") ?? TelemetryConstants.DefaultSentryDsn;
        set => _dsnEndpoint = value;
    }

    /// <inheritdoc/>
    public bool CanHandle(TelemetryEvent telemetryEvent)
    {
        ArgumentNullException.ThrowIfNull(telemetryEvent);
        return telemetryEvent.EventName == TelemetryConstants.Events.AppCrash ||
               telemetryEvent.Level == TelemetryLevel.CrashReportsOnly;
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> EmitAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(telemetryEvent);

        if (!CanHandle(telemetryEvent))
        {
            return OperationResult<bool>.CreateSuccess(false);
        }

        var dsn = DsnEndpoint;
        if (string.IsNullOrWhiteSpace(dsn) || httpClient == null)
        {
            // Buffer locally if unconfigured
            _crashBuffer.Enqueue(telemetryEvent);
            while (_crashBuffer.Count > 50)
            {
                _crashBuffer.TryDequeue(out _);
            }

            return OperationResult<bool>.CreateSuccess(true);
        }

        try
        {
            var (storeUrl, publicKey) = ParseDsn(dsn);
            var extra = new Dictionary<string, object?>(telemetryEvent.Properties ?? new Dictionary<string, object?>());

            string? exceptionType = null;
            string? exceptionMessage = null;
            string? stackTrace = null;
            var isFatal = false;

            if (extra.Remove(TelemetryConstants.Properties.ExceptionType, out var exTypeObj) && exTypeObj != null)
            {
                exceptionType = exTypeObj.ToString();
            }

            if (extra.Remove(TelemetryConstants.Properties.ExceptionMessage, out var exMsgObj) && exMsgObj != null)
            {
                exceptionMessage = exMsgObj.ToString();
            }

            if (extra.Remove(TelemetryConstants.Properties.StackTrace, out var stackObj) && stackObj != null)
            {
                stackTrace = stackObj.ToString();
            }

            if (extra.Remove(TelemetryConstants.Properties.IsFatal, out var fatalObj) && fatalObj is bool b)
            {
                isFatal = b;
            }

            var payload = new Dictionary<string, object?>
            {
                ["event_id"] = Guid.NewGuid().ToString("N"),
                ["timestamp"] = telemetryEvent.Timestamp.ToString("o"),
                ["platform"] = "csharp",
                ["level"] = isFatal ? "fatal" : "error",
                ["logger"] = TelemetryConstants.AppName,
                ["release"] = telemetryEvent.AppVersion,
                ["environment"] = AppConstants.BuildChannel,
                ["tags"] = new Dictionary<string, string>
                {
                    ["os"] = telemetryEvent.Platform,
                    ["arch"] = RuntimeInformation.ProcessArchitecture.ToString(),
                },
                ["user"] = new Dictionary<string, string>
                {
                    ["id"] = telemetryEvent.InstallationId,
                },
                ["extra"] = extra,
            };

            if (!string.IsNullOrEmpty(exceptionMessage) || !string.IsNullOrEmpty(exceptionType))
            {
                payload["message"] = new Dictionary<string, object?>
                {
                    ["formatted"] = exceptionMessage ?? exceptionType ?? "Application Crash",
                };

                payload["exception"] = new Dictionary<string, object?>
                {
                    ["values"] = new[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["type"] = exceptionType ?? "Exception",
                            ["value"] = exceptionMessage ?? string.Empty,
                            ["stacktrace"] = !string.IsNullOrEmpty(stackTrace)
                                ? new Dictionary<string, object?> { ["raw"] = stackTrace }
                                : null,
                        },
                    },
                };
            }

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var request = new HttpRequestMessage(HttpMethod.Post, storeUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };

            if (!string.IsNullOrEmpty(publicKey))
            {
                var unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var authHeader = $"Sentry sentry_version=7, sentry_client={TelemetryConstants.AppName}/{telemetryEvent.AppVersion}, sentry_key={publicKey}, sentry_timestamp={unixTimestamp}";
                request.Headers.TryAddWithoutValidation("X-Sentry-Auth", authHeader);
            }

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return OperationResult<bool>.CreateSuccess(true);
            }

            logger.LogDebug("[Sentry] Crash endpoint returned status code {StatusCode}", response.StatusCode);
            _crashBuffer.Enqueue(telemetryEvent);
            return OperationResult<bool>.CreateFailure($"Crash endpoint returned {response.StatusCode}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[Sentry] Failed to send crash report to Sentry endpoint");
            _crashBuffer.Enqueue(telemetryEvent);
            return OperationResult<bool>.CreateFailure(ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> FlushAsync(CancellationToken cancellationToken = default)
    {
        if (httpClient == null || _crashBuffer.IsEmpty)
        {
            return OperationResult<bool>.CreateSuccess(true);
        }

        var failed = false;
        var count = _crashBuffer.Count;
        for (var i = 0; i < count && _crashBuffer.TryDequeue(out var ev); i++)
        {
            var res = await EmitAsync(ev, cancellationToken);
            if (!res.Success)
            {
                failed = true;
            }
        }

        return failed
            ? OperationResult<bool>.CreateFailure("Failed to flush some buffered crash events")
            : OperationResult<bool>.CreateSuccess(true);
    }

    private static (string StoreUrl, string? PublicKey) ParseDsn(string dsn)
    {
        if (string.IsNullOrWhiteSpace(dsn))
        {
            return (string.Empty, null);
        }

        if (!Uri.TryCreate(dsn, UriKind.Absolute, out var uri))
        {
            return (dsn, null);
        }

        if (string.IsNullOrEmpty(uri.UserInfo))
        {
            return (dsn, null);
        }

        var publicKey = uri.UserInfo;
        var projectId = uri.AbsolutePath.Trim('/');
        var storeUrl = $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? string.Empty : $":{uri.Port}")}/api/{projectId}/store/";

        return (storeUrl, publicKey);
    }
}

