using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Online;
using GenHub.Core.Models.Online;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Online.Services;

/// <summary>
/// UDP-based P2P connection service with STUN reflexive endpoint discovery.
/// Hole-punch rendezvous stays join-gated: no STUN traffic happens while browsing.
/// </summary>
/// <param name="logger">The logger.</param>
public sealed class P2PConnectionService(ILogger<P2PConnectionService> logger) : IP2PConnectionService, IDisposable
{
    private readonly object _syncLock = new();
    private readonly Dictionary<string, TaskCompletionSource<bool>> _pendingProbes = new();
    private UdpClient? _listener;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private bool _disposed;

    /// <inheritdoc/>
    public OnlineConnectionQuality CurrentQuality { get; private set; } = OnlineConnectionQuality.Unknown;

    /// <inheritdoc/>
    public event EventHandler<OnlineConnectionQuality>? ConnectionStatusChanged;

    /// <inheritdoc/>
    public Task<OperationResult<IPEndPoint>> StartListeningAsync(int port, CancellationToken cancellationToken = default)
    {
        if (port is < 0 or > 65535)
        {
            return Task.FromResult(OperationResult<IPEndPoint>.CreateFailure("Port is out of range."));
        }

        IPEndPoint endpoint;
        lock (_syncLock)
        {
            ThrowIfDisposed();
            try
            {
                StopReceiveLoopLocked();
                _listener?.Close();
                _listener = new UdpClient(port);
                endpoint = (IPEndPoint)_listener.Client.LocalEndPoint!;
                StartReceiveLoopLocked(_listener);
            }
            catch (SocketException ex)
            {
                logger.LogWarning(ex, "Failed to bind P2P listener on port {Port}.", port);
                return Task.FromResult(OperationResult<IPEndPoint>.CreateFailure("Failed to bind local port."));
            }
        }

        SetQuality(OnlineConnectionQuality.Connecting);
        return Task.FromResult(OperationResult<IPEndPoint>.CreateSuccess(endpoint));
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> ConnectToPeerAsync(
        string ipAddress,
        int port,
        CancellationToken cancellationToken = default)
    {
        if (!IPAddress.TryParse(ipAddress, out var address) || port is < 1 or > 65535)
        {
            return OperationResult<bool>.CreateFailure("Peer endpoint is invalid.");
        }

        UdpClient? listener;
        lock (_syncLock)
        {
            ThrowIfDisposed();
            listener = _listener;
        }

        if (listener is null)
        {
            return OperationResult<bool>.CreateFailure("Listener is not started.");
        }

        try
        {
            var target = new IPEndPoint(address, port);
            var punch = OnlineConstants.GetPunchMagic();
            for (var i = 0; i < OnlineConstants.PunchPacketCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await listener.SendAsync(punch, target, cancellationToken);
            }

            // Sends are unacknowledged; stay in Connecting until inbound
            // traffic confirms the peer is reachable.
            SetQuality(OnlineConnectionQuality.Connecting);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (SocketException ex)
        {
            logger.LogWarning(ex, "Punch packets failed.");
            SetQuality(OnlineConnectionQuality.Unknown);
            return OperationResult<bool>.CreateFailure("Failed to reach peer.");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<P2PEndpoints>> GetLocalAndPublicEndpointsAsync(
        CancellationToken cancellationToken = default)
    {
        IPEndPoint local;
        lock (_syncLock)
        {
            ThrowIfDisposed();
            if (_listener?.Client.LocalEndPoint is not IPEndPoint bound)
            {
                return OperationResult<P2PEndpoints>.CreateFailure("Listener is not started.");
            }

            local = bound;
        }

        try
        {
            var publicEndpoint = await QueryStunAsync(cancellationToken);
            return OperationResult<P2PEndpoints>.CreateSuccess(new P2PEndpoints(local, publicEndpoint));
        }
        catch (Exception ex) when (ex is SocketException or TimeoutException)
        {
            logger.LogWarning(ex, "STUN query failed: {Message}", OnlineLogScrubber.Scrub(ex.Message));
            return OperationResult<P2PEndpoints>.CreateSuccess(new P2PEndpoints(local, null));
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> ProbePeerAsync(
        string ipAddress,
        int port,
        CancellationToken cancellationToken = default)
    {
        if (!IPAddress.TryParse(ipAddress, out var address) || port is < 1 or > 65535)
        {
            return OperationResult<bool>.CreateFailure("Peer endpoint is invalid.");
        }

        UdpClient? listener;
        lock (_syncLock)
        {
            ThrowIfDisposed();
            listener = _listener;
        }

        if (listener is null)
        {
            return OperationResult<bool>.CreateFailure("Listener is not started.");
        }

        var target = new IPEndPoint(address, port);
        for (var attempt = 0; attempt < OnlineConstants.MeshCheckAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await TryProbeOnceAsync(listener, target, cancellationToken))
            {
                return OperationResult<bool>.CreateSuccess(true);
            }
        }

        // Unreachable is a mesh result, not an operation error.
        return OperationResult<bool>.CreateSuccess(false);
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> StopListeningAsync(CancellationToken cancellationToken = default)
    {
        Task? receive;
        Dictionary<string, TaskCompletionSource<bool>> pending;
        lock (_syncLock)
        {
            receive = _receiveTask;
            StopReceiveLoopLocked();
            _listener?.Close();
            _listener = null;
            pending = TakePendingProbesLocked();
        }

        FailProbes(pending);
        if (receive is not null)
        {
            try
            {
                await receive.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The loop still ends via its own CTS and socket close.
            }
        }

        SetQuality(OnlineConnectionQuality.Unknown);
        return OperationResult<bool>.CreateSuccess(true);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dictionary<string, TaskCompletionSource<bool>> pending;
        lock (_syncLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopReceiveLoopLocked();
            _listener?.Close();
            _listener = null;
            pending = TakePendingProbesLocked();
        }

        FailProbes(pending);
    }

    /// <summary>
    /// Orders STUN server candidates so IPv4 addresses are tried before IPv6.
    /// </summary>
    /// <param name="addresses">The resolved server addresses.</param>
    /// <returns>The addresses with IPv4 first.</returns>
    internal static IReadOnlyList<IPAddress> OrderStunCandidates(IPAddress[] addresses)
    {
        return addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).ToList();
    }

    private static bool IsPunchPacket(byte[] buffer)
    {
        if (buffer.Length != 4 && buffer.Length != 4 + OnlineConstants.MeshProbeTokenBytes)
        {
            return false;
        }

        var magic = OnlineConstants.GetPunchMagic();
        for (var i = 0; i < 4; i++)
        {
            if (buffer[i] != magic[i])
            {
                return false;
            }
        }

        return true;
    }

    private static void FailProbes(Dictionary<string, TaskCompletionSource<bool>> pending)
    {
        foreach (var completion in pending.Values)
        {
            completion.TrySetResult(false);
        }
    }

    private static byte[] BuildBindingRequest(out byte[] transactionId)
    {
        transactionId = RandomNumberGenerator.GetBytes(12);

        var request = new byte[20];
        request[0] = (byte)(OnlineConstants.StunBindingRequest >> 8);
        request[1] = (byte)OnlineConstants.StunBindingRequest;
        request[4] = (byte)((OnlineConstants.StunMagicCookie >> 24) & 0xFF);
        request[5] = (byte)((OnlineConstants.StunMagicCookie >> 16) & 0xFF);
        request[6] = (byte)((OnlineConstants.StunMagicCookie >> 8) & 0xFF);
        request[7] = (byte)(OnlineConstants.StunMagicCookie & 0xFF);
        Buffer.BlockCopy(transactionId, 0, request, 8, 12);
        return request;
    }

    private static IPEndPoint? ParseBindingResponse(byte[] response, byte[] transactionId)
    {
        if (response.Length < 20 || response[0] != ((OnlineConstants.StunBindingResponse >> 8) & 0xFF) || response[1] != (OnlineConstants.StunBindingResponse & 0xFF))
        {
            return null;
        }

        for (var i = 0; i < 12; i++)
        {
            if (response[8 + i] != transactionId[i])
            {
                return null;
            }
        }

        var offset = 20;
        while (offset + 4 <= response.Length)
        {
            var type = (ushort)((response[offset] << 8) | response[offset + 1]);
            var length = (ushort)((response[offset + 2] << 8) | response[offset + 3]);
            if (type == OnlineConstants.StunXorMappedAddress && offset + 4 + length <= response.Length)
            {
                return ParseXorMappedAddress(response, offset + 4, length);
            }

            offset += 4 + ((length + 3) & ~3);
        }

        return null;
    }

    private static IPEndPoint? ParseXorMappedAddress(byte[] buffer, int offset, int length)
    {
        if (length < 8 || buffer[offset + 1] != 0x01)
        {
            return null;
        }

        var port = (ushort)(((buffer[offset + 2] << 8) | buffer[offset + 3]) ^ (OnlineConstants.StunMagicCookie >> 16));
        var address = new byte[4];
        for (var i = 0; i < 4; i++)
        {
            address[i] = (byte)(buffer[offset + 4 + i] ^ (OnlineConstants.StunMagicCookie >> (24 - (i * 8))));
        }

        return new IPEndPoint(new IPAddress(address), port);
    }

    private static async Task<IPEndPoint?> TryQueryStunServerAsync(
        IPAddress address,
        byte[] request,
        byte[] transactionId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var udp = new UdpClient(address.AddressFamily);
            await udp.SendAsync(request, new IPEndPoint(address, OnlineConstants.StunPort), cancellationToken);
            var result = await udp.ReceiveAsync(cancellationToken);
            return ParseBindingResponse(result.Buffer, transactionId);
        }
        catch (SocketException)
        {
            return null;
        }
    }

    private static async Task<IPEndPoint?> QueryStunAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(OnlineConstants.StunTimeoutSeconds));

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(ApiConstants.OnlineStunHost, timeoutCts.Token);
            var request = BuildBindingRequest(out var transactionId);
            foreach (var address in OrderStunCandidates(addresses))
            {
                timeoutCts.Token.ThrowIfCancellationRequested();
                var endpoint = await TryQueryStunServerAsync(address, request, transactionId, timeoutCts.Token);
                if (endpoint is not null)
                {
                    return endpoint;
                }
            }

            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private void StartReceiveLoopLocked(UdpClient listener)
    {
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        _receiveCts = cts;
        _receiveTask = Task.Run(() => ReceiveLoopAsync(listener, token), CancellationToken.None);
    }

    private void StopReceiveLoopLocked()
    {
        try
        {
            _receiveCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already torn down.
        }

        _receiveCts?.Dispose();
        _receiveCts = null;
        _receiveTask = null;
    }

    private Dictionary<string, TaskCompletionSource<bool>> TakePendingProbesLocked()
    {
        var pending = new Dictionary<string, TaskCompletionSource<bool>>(_pendingProbes);
        _pendingProbes.Clear();
        return pending;
    }

    private void CompleteProbe(byte[] buffer)
    {
        if (buffer.Length <= 4)
        {
            // Bare legacy punch: echoed, but nothing waits on it.
            return;
        }

        var key = Convert.ToBase64String(buffer, 4, buffer.Length - 4);
        TaskCompletionSource<bool>? pending;
        lock (_syncLock)
        {
            _pendingProbes.TryGetValue(key, out pending);
            _pendingProbes.Remove(key);
        }

        pending?.TrySetResult(true);
    }

    private async Task ReceiveLoopAsync(UdpClient listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await listener.ReceiveAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or OperationCanceledException)
            {
                return;
            }

            await HandleDatagramAsync(listener, result, cancellationToken);
        }
    }

    private async Task HandleDatagramAsync(UdpClient listener, UdpReceiveResult result, CancellationToken cancellationToken)
    {
        if (!IsPunchPacket(result.Buffer))
        {
            return;
        }

        try
        {
            await listener.SendAsync(result.Buffer, result.RemoteEndPoint, cancellationToken);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException or OperationCanceledException)
        {
            return;
        }

        CompleteProbe(result.Buffer);
    }

    private async Task<bool> TryProbeOnceAsync(UdpClient listener, IPEndPoint target, CancellationToken cancellationToken)
    {
        var token = RandomNumberGenerator.GetBytes(OnlineConstants.MeshProbeTokenBytes);
        var magic = OnlineConstants.GetPunchMagic();
        var probe = new byte[magic.Length + token.Length];
        Buffer.BlockCopy(magic, 0, probe, 0, magic.Length);
        Buffer.BlockCopy(token, 0, probe, magic.Length, token.Length);

        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var key = Convert.ToBase64String(token);
        lock (_syncLock)
        {
            ThrowIfDisposed();
            _pendingProbes[key] = pending;
        }

        try
        {
            await listener.SendAsync(probe, target, cancellationToken);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(OnlineConstants.MeshProbeTimeoutMs);
            try
            {
                return await pending.Task.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Probe timeout: the caller spends the next attempt.
                return false;
            }
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            return false;
        }
        finally
        {
            lock (_syncLock)
            {
                _pendingProbes.Remove(key);
            }
        }
    }

    private void SetQuality(OnlineConnectionQuality quality)
    {
        lock (_syncLock)
        {
            CurrentQuality = quality;
        }

        // Invoke outside the lock: subscriber code must never run under it.
        ConnectionStatusChanged?.Invoke(this, quality);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
