using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Online;
using GenHub.Core.Models.Online;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Online.Services;

/// <summary>
/// WebSocket presence channel: live roster fan-out with heartbeat and
/// reconnect with backoff. Application close codes (evicted, banned) are
/// terminal and surface as <see cref="ConnectionLost"/>. The join grant is
/// proactively refreshed before expiry so reconnects never 403.
/// </summary>
/// <param name="httpClientFactory">The HTTP client factory.</param>
/// <param name="logger">The logger.</param>
public sealed class OnlinePresenceService(
    IHttpClientFactory httpClientFactory,
    ILogger<OnlinePresenceService> logger) : IOnlinePresenceService, IDisposable, IAsyncDisposable
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
    private DateTime _grantExpiresUtc;
    private bool _disposed;

    /// <inheritdoc/>
    public event EventHandler<IReadOnlyList<OnlineMember>>? RosterUpdated;

    /// <inheritdoc/>
    public event EventHandler? ConnectionLost;

    /// <inheritdoc/>
    public event EventHandler<string>? GrantRefreshed;

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
        DateTime grantExpiresUtc = default,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(networkId) || string.IsNullOrWhiteSpace(grant))
        {
            return OperationResult<bool>.CreateFailure("Network id and grant are required.");
        }

        ThrowIfDisposed();
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await StopLoopAsync().ConfigureAwait(false);

            _networkId = networkId;
            _grant = grant;
            _grantExpiresUtc = grantExpiresUtc;
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
        ThrowIfDisposed();
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopLoopAsync().ConfigureAwait(false);
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

        // Serialize with lifecycle methods: a concurrent Connect/Disconnect
        // must finish before the loop is torn down and the semaphore is
        // disposed. Every await below uses ConfigureAwait(false), so blocking
        // here cannot deadlock against a captured UI context at shutdown.
        // Disposal is never cancellable, hence CancellationToken.None.
        _stateLock.Wait(CancellationToken.None);
        try
        {
            StopLoopAsync().GetAwaiter().GetResult();
        }
        finally
        {
            _stateLock.Release();
        }

        _stateLock.Dispose();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        lock (_syncLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        await _stateLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await StopLoopAsync().ConfigureAwait(false);
        }
        finally
        {
            _stateLock.Release();
        }

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

    /// <summary>
    /// Determines whether the join grant must be re-minted before connecting.
    /// </summary>
    /// <param name="grantExpiresUtc">The grant expiry, or default when unknown.</param>
    /// <param name="utcNow">The current UTC time.</param>
    /// <param name="attempt">The reconnect attempt number.</param>
    /// <returns>True when the grant must be refreshed first.</returns>
    internal static bool GrantNeedsRefresh(DateTime grantExpiresUtc, DateTime utcNow, int attempt)
    {
        if (grantExpiresUtc == default)
        {
            // Unknown expiry: the opening grant is fresh from join, but every
            // later reconnect re-mints to be safe.
            return attempt > 0;
        }

        return grantExpiresUtc - utcNow < TimeSpan.FromSeconds(OnlineConstants.GrantRefreshLeadTimeSeconds);
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
        // Only the server's explicit membership terminations end the session.
        // Transient closes (1001 GoingAway during restarts, network drops)
        // must reconnect with backoff instead of auto-leaving the network.
        return status is (WebSocketCloseStatus)4000 or (WebSocketCloseStatus)4001;
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            ClientWebSocket? socket = null;
            try
            {
                // The edge re-verifies the ticket on every upgrade and grants
                // live minutes: re-mint before connecting, never after a 403
                // (an expired grant cannot refresh itself).
                if (GrantNeedsRefresh(_grantExpiresUtc, DateTime.UtcNow, attempt)
                    && !await RefreshGrantAsync(_networkId, _grant, cancellationToken).ConfigureAwait(false))
                {
                    ConnectionLost?.Invoke(this, EventArgs.Empty);
                    return;
                }

                var uri = BuildPresenceUri(ApiConstants.OnlineEdgeBaseUrl, _networkId, _grant);
                socket = new ClientWebSocket();
                ClientWebSocket? previous;
                lock (_syncLock)
                {
                    previous = _socket;
                    _socket = socket;
                }

                previous?.Dispose();

                await socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
                attempt = 0;
                var terminal = await ReceiveLoopAsync(socket, cancellationToken).ConfigureAwait(false);
                if (terminal)
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                DisposeIfUnowned(socket);
                return;
            }
            catch (WebSocketException ex)
            {
                logger.LogWarning("Presence connection failed: {Message}", OnlineLogScrubber.Scrub(ex.Message));
            }

            attempt++;
            try
            {
                await Task.Delay(BackoffDelay(attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<bool> RefreshGrantAsync(string networkId, string grant, CancellationToken cancellationToken)
    {
        try
        {
            using var client = httpClientFactory.CreateClient(nameof(OnlinePresenceService));
            client.BaseAddress = new Uri(ApiConstants.OnlineEdgeBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(OnlineConstants.HttpTimeoutSeconds);
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                string.Format(ApiConstants.OnlineCertFormat, Uri.EscapeDataString(networkId)));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", grant);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                // Removed or banned: the grant cannot be renewed.
                return false;
            }

            if (!response.IsSuccessStatusCode)
            {
                // Transient edge failure: proceed with the current grant.
                return true;
            }

            var cert = await response.Content.ReadFromJsonAsync<OnlineCertResult>(cancellationToken).ConfigureAwait(false);
            if (cert is null || string.IsNullOrWhiteSpace(cert.Grant))
            {
                return true;
            }

            lock (_syncLock)
            {
                _grant = cert.Grant;
                _grantExpiresUtc = cert.GrantExpiresUtc;
            }

            GrantRefreshed?.Invoke(this, cert.Grant);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Grant refresh failed; keeping the current grant.");
            return true;
        }
        catch (TaskCanceledException)
        {
            // Timeout rather than shutdown: best effort.
            return true;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private void DisposeIfUnowned(ClientWebSocket? socket)
    {
        if (socket is null)
        {
            return;
        }

        lock (_syncLock)
        {
            if (_socket == socket)
            {
                return;
            }
        }

        socket.Dispose();
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
                var result = await socket.ReceiveAsync(_receiveBuffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await HandleCloseAsync(socket, result, cancellationToken).ConfigureAwait(false);
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
            await heartbeatCts.CancelAsync().ConfigureAwait(false);
            try
            {
                await heartbeatTask.ConfigureAwait(false);
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
                await Task.Delay(TimeSpan.FromSeconds(OnlineConstants.PresenceHeartbeatSeconds), cancellationToken).ConfigureAwait(false);
                if (socket.State == WebSocketState.Open)
                {
                    await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
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
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown raced the close handshake.
        }
        catch (WebSocketException)
        {
            // Socket already gone; nothing to acknowledge.
        }
        catch (InvalidOperationException)
        {
            // A heartbeat send holds the single outstanding-send slot;
            // the close handshake is best effort.
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
            await _loopCts.CancelAsync().ConfigureAwait(false);
        }

        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }

            _loopTask = null;
        }

        lock (_syncLock)
        {
            _socket?.Dispose();
            _socket = null;
        }

        _loopCts?.Dispose();
        _loopCts = null;
        _networkId = string.Empty;
        _grant = string.Empty;
        _grantExpiresUtc = default;
    }

    private void ThrowIfDisposed()
    {
        lock (_syncLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }
}
