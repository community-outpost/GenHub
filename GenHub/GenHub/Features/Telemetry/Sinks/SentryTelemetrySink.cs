using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Telemetry;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Telemetry;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Telemetry.Sinks;

/// <summary>
/// Telemetry sink for delivering unhandled exceptions and crash forensics to Sentry or crash endpoints.
/// </summary>
public sealed class SentryTelemetrySink(
    ILogger<SentryTelemetrySink> logger,
    HttpClient? httpClient = null) : ITelemetrySink
{
    private const int MaxBufferSize = 50;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ConcurrentQueue<TelemetryEvent> _crashBuffer = new();

    /// <inheritdoc/>
    public string Name => "Sentry";

    /// <summary>
    /// Gets or sets the Sentry DSN or HTTP crash reporting endpoint.
    /// When null or empty, defaults to the configured default Sentry DSN or buffers locally.
    /// </summary>
    public string? DsnEndpoint { get; set; } = Environment.GetEnvironmentVariable("SENTRY_DSN") ?? Environment.GetEnvironmentVariable("GENHUB_SENTRY_DSN") ?? TelemetryConstants.DefaultSentryDsn;

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
            EnqueueBounded(telemetryEvent);
            return OperationResult<bool>.CreateSuccess(true);
        }

        try
        {
            var (storeUrl, publicKey) = ParseDsn(dsn);
            var payload = BuildSentryPayload(telemetryEvent);

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
            EnqueueBounded(telemetryEvent);
            return OperationResult<bool>.CreateFailure($"Crash endpoint returned {response.StatusCode}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[Sentry] Failed to send crash report to Sentry endpoint");
            EnqueueBounded(telemetryEvent);
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
            try
            {
                var res = await EmitAsync(ev, cancellationToken);
                if (!res.Success)
                {
                    failed = true;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                EnqueueBounded(ev);
                throw;
            }
        }

        return failed
            ? OperationResult<bool>.CreateFailure("Failed to flush some buffered crash events")
            : OperationResult<bool>.CreateSuccess(true);
    }

    private static Dictionary<string, object?> BuildSentryPayload(TelemetryEvent telemetryEvent)
    {
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

        if (extra.TryGetValue(TelemetryConstants.Properties.StackTrace, out var stackObj) && stackObj != null)
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
            ["release"] = telemetryEvent.AppVersion ?? string.Empty,
            ["environment"] = AppConstants.BuildChannel,
            ["tags"] = new Dictionary<string, string>
            {
                ["os"] = telemetryEvent.Platform ?? string.Empty,
                ["arch"] = RuntimeInformation.ProcessArchitecture.ToString(),
            },
            ["user"] = new Dictionary<string, string>
            {
                ["id"] = telemetryEvent.InstallationId ?? string.Empty,
            },
            ["extra"] = extra,
        };

        if (!string.IsNullOrEmpty(exceptionMessage) || !string.IsNullOrEmpty(exceptionType))
        {
            payload["message"] = new Dictionary<string, object?>
            {
                ["formatted"] = exceptionMessage ?? exceptionType ?? "Application Crash",
            };

            var exceptionDict = new Dictionary<string, object?>
            {
                ["type"] = exceptionType ?? "Exception",
                ["value"] = exceptionMessage ?? string.Empty,
            };

            var parsedStack = ParseSentryStackTrace(stackTrace);
            if (parsedStack.Count > 0)
            {
                exceptionDict["stacktrace"] = parsedStack;
            }

            payload["exception"] = new Dictionary<string, object?>
            {
                ["values"] = new[] { exceptionDict },
            };
        }

        return payload;
    }

    private static Dictionary<string, object?> ParseSentryStackTrace(string? stackTrace)
    {
        if (string.IsNullOrWhiteSpace(stackTrace))
        {
            return new Dictionary<string, object?>();
        }

        var lines = stackTrace.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var frames = new List<Dictionary<string, object?>>();

        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var frame = TryParseStackFrame(lines[i]);
            if (frame != null)
            {
                frames.Add(frame);
            }
        }

        if (frames.Count == 0)
        {
            return new Dictionary<string, object?>();
        }

        return new Dictionary<string, object?>
        {
            ["frames"] = frames,
        };
    }

    private static Dictionary<string, object?>? TryParseStackFrame(string rawLine)
    {
        var line = rawLine.Trim();
        if (!line.StartsWith("at ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        line = line[3..].Trim();
        string function = line;
        string? filename = null;
        int? lineno = null;

        var inIdx = line.IndexOf(" in ", StringComparison.Ordinal);
        if (inIdx >= 0)
        {
            function = line[..inIdx].Trim();
            (filename, lineno) = ParseSourceLocation(line[(inIdx + 4)..].Trim());
        }

        var inApp = !function.StartsWith("System.", StringComparison.OrdinalIgnoreCase) &&
                    !function.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase);

        var frame = new Dictionary<string, object?>
        {
            ["function"] = function,
            ["in_app"] = inApp,
        };

        if (!string.IsNullOrEmpty(filename))
        {
            frame["filename"] = filename;
        }

        if (lineno.HasValue)
        {
            frame["lineno"] = lineno.Value;
        }

        return frame;
    }

    private static (string? Filename, int? LineNo) ParseSourceLocation(string fileAndLine)
    {
        var lineIdx = fileAndLine.LastIndexOf(":line ", StringComparison.OrdinalIgnoreCase);
        if (lineIdx < 0)
        {
            return (fileAndLine, null);
        }

        var filename = fileAndLine[..lineIdx].Trim();
        return int.TryParse(fileAndLine[(lineIdx + 6)..].Trim(), out var parsedLine)
            ? (filename, parsedLine)
            : (filename, null);
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

    private void EnqueueBounded(TelemetryEvent telemetryEvent)
    {
        _crashBuffer.Enqueue(telemetryEvent);
        while (_crashBuffer.Count > MaxBufferSize)
        {
            _crashBuffer.TryDequeue(out _);
        }
    }
}
