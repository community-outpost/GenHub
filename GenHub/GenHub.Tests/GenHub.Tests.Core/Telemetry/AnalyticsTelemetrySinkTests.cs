using System;
using System.Collections.Generic;
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
/// Unit tests for <see cref="AnalyticsTelemetrySink"/>.
/// </summary>
public class AnalyticsTelemetrySinkTests
{
    private readonly Mock<ILogger<AnalyticsTelemetrySink>> _loggerMock = new();
    private readonly AnalyticsTelemetrySink _sink;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnalyticsTelemetrySinkTests"/> class.
    /// </summary>
    public AnalyticsTelemetrySinkTests()
    {
        _sink = new AnalyticsTelemetrySink(_loggerMock.Object);
    }

    /// <summary>
    /// Verifies sink metadata and CanHandle predicate.
    /// </summary>
    [Fact]
    public void CanHandle_OnlyHandlesAnonymousMetricsEvents()
    {
        var anonymousEvent = new TelemetryEvent
        {
            EventName = TelemetryConstants.Events.GameSessionStarted,
            Level = TelemetryLevel.AnonymousMetrics,
        };

        var crashEvent = new TelemetryEvent
        {
            EventName = TelemetryConstants.Events.AppCrash,
            Level = TelemetryLevel.CrashReportsOnly,
        };

        Assert.True(_sink.CanHandle(anonymousEvent));
        Assert.False(_sink.CanHandle(crashEvent));
    }

    /// <summary>
    /// Verifies EndpointUrl and ApiKey properties default to configured PostHog constants.
    /// </summary>
    [Fact]
    public void EndpointUrlAndApiKey_DefaultToPostHogConstants()
    {
        Assert.Equal(TelemetryConstants.DefaultPostHogCaptureEndpoint, _sink.EndpointUrl);
        Assert.Equal(TelemetryConstants.DefaultPostHogApiKey, _sink.ApiKey);
    }

    /// <summary>
    /// Verifies EmitAsync succeeds and buffers locally when no HTTP client is configured.
    /// </summary>
    [Fact]
    public async Task EmitAsync_WhenNoHttpClient_BuffersAndReturnsSuccess()
    {
        var ev = new TelemetryEvent
        {
            EventName = TelemetryConstants.Events.ContentDownloadCompleted,
            Level = TelemetryLevel.AnonymousMetrics,
            Properties = new Dictionary<string, object?>
            {
                [TelemetryConstants.Properties.SizeMb] = 450.0,
                [TelemetryConstants.Properties.DurationSeconds] = 12.5,
            },
        };

        var result = await _sink.EmitAsync(ev);
        Assert.True(result.Success);
    }

    /// <summary>
    /// Verifies EmitAsync sends request formatted for PostHog capture API.
    /// </summary>
    [Fact]
    public async Task EmitAsync_WhenHttpClientProvided_SendsPostHogFormattedPayload()
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
        var sink = new AnalyticsTelemetrySink(_loggerMock.Object, client);

        var ev = new TelemetryEvent
        {
            EventName = TelemetryConstants.Events.GameSessionStarted,
            Level = TelemetryLevel.AnonymousMetrics,
            InstallationId = "inst-9999",
            SessionId = "sess-7777",
            AppVersion = "1.0.0",
            Platform = "Linux",
            Properties = new Dictionary<string, object?>
            {
                [TelemetryConstants.Properties.GameType] = "ZeroHour",
            },
        };

        var result = await sink.EmitAsync(ev);

        Assert.True(result.Success);
        Assert.NotNull(capturedRequest);
        Assert.Equal(TelemetryConstants.DefaultPostHogCaptureEndpoint, capturedRequest.RequestUri?.ToString());

        Assert.NotNull(capturedBody);
        using var jsonDoc = JsonDocument.Parse(capturedBody);
        Assert.Equal(TelemetryConstants.DefaultPostHogApiKey, jsonDoc.RootElement.GetProperty("api_key").GetString());
        Assert.Equal(TelemetryConstants.Events.GameSessionStarted, jsonDoc.RootElement.GetProperty("event").GetString());
        Assert.Equal("inst-9999", jsonDoc.RootElement.GetProperty("distinct_id").GetString());

        var properties = jsonDoc.RootElement.GetProperty("properties");
        Assert.Equal("GenHub", properties.GetProperty("$lib").GetString());
        Assert.Equal("sess-7777", properties.GetProperty("$session_id").GetString());
        Assert.Equal("ZeroHour", properties.GetProperty(TelemetryConstants.Properties.GameType).GetString());
    }

    /// <summary>
    /// Verifies EmitAsync buffers and returns failure when endpoint returns error.
    /// </summary>
    [Fact]
    public async Task EmitAsync_WhenEndpointReturnsError_BuffersAndReturnsFailure()
    {
        var handler = new TestHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)));
        using var client = new HttpClient(handler);
        var sink = new AnalyticsTelemetrySink(_loggerMock.Object, client);

        var ev = new TelemetryEvent
        {
            EventName = TelemetryConstants.Events.GameSessionStarted,
            Level = TelemetryLevel.AnonymousMetrics,
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
        var handler = new TestHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var client = new HttpClient(handler);
        var sink = new AnalyticsTelemetrySink(_loggerMock.Object, client);

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

