using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Telemetry;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Telemetry;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Telemetry.Services;

/// <summary>
/// Core telemetry service that manages the bounded event queue, client-side scrubbing, breadcrumbs, and sink dispatching.
/// </summary>
public sealed class TelemetryService : ITelemetryService, IAsyncDisposable, IDisposable
{
    private readonly ILogger<TelemetryService> _logger;
    private readonly ITelemetrySanitizer _sanitizer;
    private readonly IUserSettingsService _userSettingsService;
    private readonly IReadOnlyList<ITelemetrySink> _sinks;

    private readonly Channel<TelemetryEvent> _channel;
    private readonly ConcurrentQueue<Breadcrumb> _breadcrumbs = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processingTask;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TelemetryService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="sanitizer">The telemetry data sanitizer.</param>
    /// <param name="userSettingsService">The user settings service.</param>
    /// <param name="sinks">The registered telemetry destination sinks.</param>
    public TelemetryService(
        ILogger<TelemetryService> logger,
        ITelemetrySanitizer sanitizer,
        IUserSettingsService userSettingsService,
        IEnumerable<ITelemetrySink> sinks)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sanitizer = sanitizer ?? throw new ArgumentNullException(nameof(sanitizer));
        _userSettingsService = userSettingsService ?? throw new ArgumentNullException(nameof(userSettingsService));
        _sinks = (sinks ?? []).ToList();

        _channel = Channel.CreateBounded<TelemetryEvent>(new BoundedChannelOptions(TelemetryConstants.MaxQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        _processingTask = Task.Run(() => ProcessChannelAsync(_cts.Token));
    }

    /// <inheritdoc/>
    public TelemetryLevel CurrentLevel
    {
        get
        {
            try
            {
                return _userSettingsService.Get().TelemetryPreference;
            }
            catch
            {
                return TelemetryLevel.Disabled;
            }
        }
    }

    /// <inheritdoc/>
    public bool IsEnabled(TelemetryLevel level)
    {
        var current = CurrentLevel;
        if (current == TelemetryLevel.Disabled)
        {
            return false;
        }

        return (int)current >= (int)level;
    }

    /// <inheritdoc/>
    public void TrackEvent(
        string eventName,
        IReadOnlyDictionary<string, object?>? properties = null,
        TelemetryLevel level = TelemetryLevel.AnonymousMetrics)
    {
        if (string.IsNullOrWhiteSpace(eventName) || !IsEnabled(level) || _disposed)
        {
            return;
        }

        try
        {
            var installationId = GetOrCreateInstallationId();
            var sanitizedProperties = _sanitizer.SanitizeProperties(properties);

            string? sessionId = null;
            if (properties?.TryGetValue(TelemetryConstants.Properties.SessionId, out var rawSessionId) is true && rawSessionId != null)
            {
                sessionId = rawSessionId.ToString();
            }

            var telemetryEvent = new TelemetryEvent
            {
                EventName = _sanitizer.SanitizeString(eventName),
                Timestamp = DateTimeOffset.UtcNow,
                Level = level,
                InstallationId = installationId,
                SessionId = sessionId,
                AppVersion = AppConstants.AppVersion,
                Platform = RuntimeInformation.OSDescription,
                Properties = sanitizedProperties,
            };

            _channel.Writer.TryWrite(telemetryEvent);
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to track telemetry event {EventName}", eventName);
        }
    }

    /// <inheritdoc/>
    public void TrackException(
        Exception exception,
        string? context = null,
        IReadOnlyDictionary<string, object?>? properties = null,
        bool isFatal = false)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (!IsEnabled(TelemetryLevel.CrashReportsOnly) || _disposed)
        {
            return;
        }

        try
        {
            var installationId = GetOrCreateInstallationId();
            var sanitizedMessage = _sanitizer.SanitizeString(exception.Message);
            var sanitizedStackTrace = _sanitizer.SanitizeStackTrace(exception.StackTrace);
            var breadcrumbs = GetRecentBreadcrumbs();

            var combinedProperties = new Dictionary<string, object?>(properties ?? new Dictionary<string, object?>())
            {
                [TelemetryConstants.Properties.ExceptionType] = exception.GetType().FullName ?? exception.GetType().Name,
                [TelemetryConstants.Properties.ExceptionMessage] = sanitizedMessage,
                [TelemetryConstants.Properties.StackTrace] = sanitizedStackTrace,
                [TelemetryConstants.Properties.IsFatal] = isFatal,
                [TelemetryConstants.Properties.Context] = context ?? "Application",
                ["breadcrumbs"] = breadcrumbs.Select(b => new
                {
                    b.Message,
                    b.Category,
                    Timestamp = b.Timestamp.ToString("o"),
                    b.Data,
                }).ToList(),
            };

            var sanitizedProperties = _sanitizer.SanitizeProperties(combinedProperties);

            var telemetryEvent = new TelemetryEvent
            {
                EventName = TelemetryConstants.Events.AppCrash,
                Timestamp = DateTimeOffset.UtcNow,
                Level = TelemetryLevel.CrashReportsOnly,
                InstallationId = installationId,
                AppVersion = AppConstants.AppVersion,
                Platform = RuntimeInformation.OSDescription,
                Properties = sanitizedProperties,
            };

            _channel.Writer.TryWrite(telemetryEvent);
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to track exception");
        }
    }

    /// <inheritdoc/>
    public void AddBreadcrumb(string message, string? category = null, IReadOnlyDictionary<string, object?>? data = null)
    {
        if (string.IsNullOrWhiteSpace(message) || _disposed)
        {
            return;
        }

        try
        {
            var breadcrumb = new Breadcrumb
            {
                Message = _sanitizer.SanitizeString(message),
                Category = category ?? "general",
                Timestamp = DateTimeOffset.UtcNow,
                Data = data != null ? _sanitizer.SanitizeProperties(data) : null,
            };

            _breadcrumbs.Enqueue(breadcrumb);

            while (_breadcrumbs.Count > TelemetryConstants.MaxBreadcrumbsCount)
            {
                _breadcrumbs.TryDequeue(out _);
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to add breadcrumb");
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<Breadcrumb> GetRecentBreadcrumbs()
    {
        return [.. _breadcrumbs];
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> FlushAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var tasks = _sinks.Select(sink => sink.FlushAsync(cancellationToken));
            await Task.WhenAll(tasks);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Error while flushing telemetry sinks");
            return OperationResult<bool>.CreateFailure(ex.Message);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel.Writer.TryComplete();
        _cts.Cancel();

        try
        {
            _processingTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Suppress background task cancellation exceptions on shutdown
        }

        _cts.Dispose();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel.Writer.TryComplete();
        _cts.Cancel();

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await _processingTask.WaitAsync(timeoutCts.Token);
            await FlushAsync(timeoutCts.Token);
        }
        catch
        {
            // Suppress background task cancellation exceptions on shutdown
        }

        _cts.Dispose();
    }

    private string GetOrCreateInstallationId()
    {
        try
        {
            var settings = _userSettingsService.Get();
            if (!string.IsNullOrWhiteSpace(settings.AnonymousInstallationId))
            {
                return settings.AnonymousInstallationId;
            }

            var newId = Guid.NewGuid().ToString("N");
            _userSettingsService.Update(s => s.AnonymousInstallationId = newId);
            return newId;
        }
        catch
        {
            return Guid.Empty.ToString("N");
        }
    }

    private async Task ProcessChannelAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (_channel.Reader.TryRead(out var telemetryEvent))
                {
                    if (telemetryEvent == null)
                    {
                        continue;
                    }

                    foreach (var sink in _sinks)
                    {
                        if (!sink.CanHandle(telemetryEvent))
                        {
                            continue;
                        }

                        try
                        {
                            await sink.EmitAsync(telemetryEvent, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogTrace(ex, "Telemetry sink {SinkName} failed emitting event", sink.Name);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Unexpected error in telemetry event processing channel");
        }
    }
}
