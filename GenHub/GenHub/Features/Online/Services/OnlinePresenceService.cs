using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Online;
using GenHub.Core.Models.Online;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Online.Services;

/// <summary>
/// WebSocket presence channel: live roster fan-out with heartbeat and
/// reconnect with backoff. Application close codes (evicted, banned) are
/// terminal and surface as <see cref="ConnectionLost"/>.
/// </summary>
/// <param name="logger">The logger.</param>
public sealed class OnlinePresenceService(ILogger<OnlinePresenceService> logger) : IOnlinePresenceService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly object _syncLock = new();
    private readonly byte[] _receiveBuffer = new byte[8192];

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private string _networkId = string.Empty;
    private string _grant = string.Empty;
    private bool _disposed;

    /// <inheritdoc/>
    public event EventHandler<IReadOnlyList<OnlineMember>>? RosterUpdated;

    /// <inheritdoc/>
    public event EventHandler? ConnectionLost;

    /// <inheritdoc/>
    public bool IsConnected
    {
        get
        {
            lock (_syncLock)
            {
                return _socket?.State == WebSocketState.Open;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> ConnectAsync(
        string networkId,
        string grant,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(networkId) || string.IsNullOrWhiteSpace(grant))
        {
            return OperationResult<bool>.CreateFailure("Network id and grant are required.");
        }

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            await StopLoopAsync();

            _networkId = networkId;
            _grant = grant;
            _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _loopTask = RunLoopAsync(_loopCts.Token);
            return OperationResult<bool>.CreateSuccess(true);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            await StopLoopAsync();
            return OperationResult<bool>.CreateSuccess(true);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _loopCts?.Cancel();
        _socket?.Dispose();
        _loopCts?.Dispose();
        _stateLock.Dispose();
    }

    /// <summary>
    /// Builds the presence socket URI for the given network and grant.
    /// </summary>
    /// <param name="edgeBaseUrl">The edge base URL.</param>
    /// <param name="networkId">The network identifier.</param>
    /// <param name="grant">The join grant ticket.</param>
    /// <returns>The presence socket URI.</returns>
    internal static Uri BuildPresenceUri(string edgeBaseUrl, string networkId, string grant)
    {
        var baseUri = new Uri(edgeBaseUrl);
        var scheme = baseUri.Scheme == "https" ? "wss" : "ws";
        var builder = new UriBuilder(scheme, baseUri.Host, baseUri.Port)
        {
            Path = string.Format(ApiConstants.OnlinePresenceFormat, Uri.EscapeDataString(networkId)),
            Query = "ticket=" + Uri.EscapeDataString(grant),
        };
        return builder.Uri;
    }

    /// <summary>
    /// Parses a roster message payload into members, or null when not a roster message.
    /// </summary>
    /// <param name="message">The raw socket message.</param>
    /// <returns>The roster members, or null.</returns>
    internal static IReadOnlyList<OnlineMember>? ParseRosterMessage(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            if (!document.RootElement.TryGetProperty("type", out var type) ||
                type.GetString() != "roster" ||
                !document.RootElement.TryGetProperty("members", out var members))
            {
                return null;
            }

            return JsonSerializer.Deserialize<IReadOnlyList<OnlineMember>>(members.GetRawText(), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TimeSpan BackoffDelay(int attempt)
    {
        var seconds = Math.Min(
            OnlineConstants.ReconnectInitialDelaySeconds * (1 << Math.Min(attempt, 5)),
            OnlineConstants.ReconnectMaxDelaySeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    private static bool IsTerminalClose(WebSocketCloseStatus? status)
    {
        return status is not null and not WebSocketCloseStatus.NormalClosure and not WebSocketCloseStatus.Empty;
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var uri = BuildPresenceUri(ApiConstants.OnlineEdgeBaseUrl, _networkId, _grant);
                var socket = new ClientWebSocket();
                lock (_syncLock)
                {
                    _socket = socket;
                }

                await socket.ConnectAsync(uri, cancellationToken);
                attempt = 0;
                var terminal = await ReceiveLoopAsync(socket, cancellationToken);
                if (terminal)
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (WebSocketException ex)
            {
                logger.LogWarning("Presence connection failed: {Message}", OnlineLogScrubber.Scrub(ex.Message));
            }

            attempt++;
            try
            {
                await Task.Delay(BackoffDelay(attempt), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<bool> ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeatTask = SendHeartbeatsAsync(socket, heartbeatCts.Token);
        try
        {
            var message = new StringBuilder();
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(_receiveBuffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await HandleCloseAsync(socket, result, cancellationToken);
                    return IsTerminalClose(result.CloseStatus);
                }

                message.Append(Encoding.UTF8.GetString(_receiveBuffer, 0, result.Count));
                if (!result.EndOfMessage)
                {
                    continue;
                }

                var roster = ParseRosterMessage(message.ToString());
                message.Clear();
                if (roster is not null)
                {
                    RosterUpdated?.Invoke(this, roster);
                }
            }

            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
        catch (WebSocketException ex)
        {
            logger.LogWarning("Presence receive failed: {Message}", OnlineLogScrubber.Scrub(ex.Message));
            return false;
        }
        finally
        {
            await heartbeatCts.CancelAsync();
            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    private async Task SendHeartbeatsAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes("""{"type":"heartbeat"}""");
        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(OnlineConstants.PresenceHeartbeatSeconds), cancellationToken);
                if (socket.State == WebSocketState.Open)
                {
                    await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        catch (WebSocketException ex)
        {
            logger.LogWarning("Presence heartbeat failed: {Message}", OnlineLogScrubber.Scrub(ex.Message));
        }
    }

    private async Task HandleCloseAsync(
        ClientWebSocket socket,
        WebSocketReceiveResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown raced the close handshake.
        }
        catch (WebSocketException)
        {
            // Socket already gone; nothing to acknowledge.
        }

        if (IsTerminalClose(result.CloseStatus))
        {
            logger.LogWarning("Presence channel closed by server: {Status}", result.CloseStatus);
            ConnectionLost?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task StopLoopAsync()
    {
        if (_loopCts is not null)
        {
            await _loopCts.CancelAsync();
        }

        if (_loopTask is not null)
        {
            try
            {
                await _loopTask;
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }

            _loopTask = null;
        }

        _socket?.Dispose();
        _socket = null;
        _loopCts?.Dispose();
        _loopCts = null;
        _networkId = string.Empty;
        _grant = string.Empty;
    }

    private void ThrowIfDisposed()
    {
        lock (_syncLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }
}
