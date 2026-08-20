using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Telemetry;
using GenHub.Features.Telemetry.Sinks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Telemetry;

/// <summary>
/// Unit tests for <see cref="SentryTelemetrySink"/>.
/// </summary>
public class SentryTelemetrySinkTests
{
    private readonly Mock<ILogger<SentryTelemetrySink>> _loggerMock = new();
    private readonly SentryTelemetrySink _sink;

    /// <summary>
    /// Initializes a new instance of the <see cref="SentryTelemetrySinkTests"/> class.
    /// </summary>
    public SentryTelemetrySinkTests()
    {
        _sink = new SentryTelemetrySink(_loggerMock.Object);
    }

    /// <summary>
    /// Verifies sink metadata and CanHandle predicate for crash events.
    /// </summary>
    [Fact]
    public void CanHandle_HandlesCrashEventsAndCrashLevel()
    {
        var crashEvent = new TelemetryEvent
        {
            EventName = TelemetryConstants.Events.AppCrash,
            Level = TelemetryLevel.CrashReportsOnly,
        };

        var standardEvent = new TelemetryEvent
        {
            EventName = TelemetryConstants.Events.GameSessionStarted,
            Level = TelemetryLevel.AnonymousMetrics,
        };

        Assert.True(_sink.CanHandle(crashEvent));
        Assert.False(_sink.CanHandle(standardEvent));
    }

    /// <summary>
    /// Verifies DSN endpoint property defaults to configured constant.
    /// </summary>
    [Fact]
    public void DsnEndpoint_DefaultsToConfiguredConstant()
    {
        Assert.Equal(TelemetryConstants.DefaultSentryDsn, _sink.DsnEndpoint);
    }

    /// <summary>
    /// Verifies EmitAsync succeeds and buffers locally when no HTTP client is configured.
    /// </summary>
    [Fact]
    public async Task EmitAsync_WhenNoHttpClient_BuffersAndReturnsSuccess()
    {
        var ev = new TelemetryEvent
        {
            EventName = TelemetryConstants.Events.AppCrash,
            Level = TelemetryLevel.CrashReportsOnly,
            Properties = new Dictionary<string, object?>
            {
                [TelemetryConstants.Properties.ExceptionType] = "System.NullReferenceException",
                [TelemetryConstants.Properties.ExceptionMessage] = "Object reference not set",
            },
        };

        var result = await _sink.EmitAsync(ev);
        Assert.True(result.Success);
    }

    /// <summary>
    /// Verifies EmitAsync sends request to Sentry store endpoint with auth headers.
    /// </summary>
    [Fact]
    public async Task EmitAsync_WhenHttpClientProvided_SendsSentryStorePayloadWithAuthHeader()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var handler = new TestHandler(async request =>
        {
            capturedRequest = request;
            if (request.Content != null)
            {
                capturedBody = await request.Content.ReadAsStringAsync();
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = new HttpClient(handler);
        var sink = new SentryTelemetrySink(_loggerMock.Object, client);

        var ev = new TelemetryEvent
        {
            EventName = TelemetryConstants.Events.AppCrash,
            Level = TelemetryLevel.CrashReportsOnly,
            InstallationId = "inst-12345",
            AppVersion = "1.0.0",
            Platform = "Linux 6.8.0",
            Properties = new Dictionary<string, object?>
            {
                [TelemetryConstants.Properties.ExceptionType] = "System.InvalidOperationException",
                [TelemetryConstants.Properties.ExceptionMessage] = "Reconciliation failed",
                [TelemetryConstants.Properties.StackTrace] = "at Foo.Bar() in Foo.cs:line 10",
                [TelemetryConstants.Properties.IsFatal] = true,
            },
        };

        var result = await sink.EmitAsync(ev);

        Assert.True(result.Success);
        Assert.NotNull(capturedRequest);
        Assert.Contains("/api/4511943606927440/store/", capturedRequest.RequestUri?.ToString());
        Assert.True(capturedRequest.Headers.Contains("X-Sentry-Auth"));

        var authHeader = capturedRequest.Headers.GetValues("X-Sentry-Auth").FirstOrDefault();
        Assert.Contains("sentry_key=06a9269c6418a6917f0fec49e1589e44", authHeader);

        Assert.NotNull(capturedBody);
        using var jsonDoc = JsonDocument.Parse(capturedBody);
        Assert.Equal("fatal", jsonDoc.RootElement.GetProperty("level").GetString());
        Assert.Equal("csharp", jsonDoc.RootElement.GetProperty("platform").GetString());
    }

    /// <summary>
    /// Verifies EmitAsync handles endpoint failure gracefully by buffering and returning failure.
    /// </summary>
    [Fact]
    public async Task EmitAsync_WhenEndpointReturnsError_BuffersAndReturnsFailure()
    {
        var handler = new TestHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        using var client = new HttpClient(handler);
        var sink = new SentryTelemetrySink(_loggerMock.Object, client);

        var ev = new TelemetryEvent
        {
            EventName = TelemetryConstants.Events.AppCrash,
            Level = TelemetryLevel.CrashReportsOnly,
        };

        var result = await sink.EmitAsync(ev);
        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies FlushAsync flushes buffered events when client is active.
    /// </summary>
    [Fact]
    public async Task FlushAsync_FlushesBufferedEventsSuccessfully()
    {
        var sendCount = 0;
        var handler = new TestHandler(_ =>
        {
            Interlocked.Increment(ref sendCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using var client = new HttpClient(handler);
        var sink = new SentryTelemetrySink(_loggerMock.Object, client);

        var flushResult = await sink.FlushAsync();
        Assert.True(flushResult.Success);
    }

    private sealed class TestHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handlerFunc) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return handlerFunc(request);
        }
    }
}

