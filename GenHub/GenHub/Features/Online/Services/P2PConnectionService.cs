using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Online;
using GenHub.Core.Models.Online;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Net.Sockets;
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
    private const int StunPort = 3473;
    private const int StunTimeoutSeconds = 5;
    private const int PunchPacketCount = 3;
    private const ushort StunBindingRequest = 0x0001;
    private const ushort StunBindingResponse = 0x0101;
    private const ushort StunXorMappedAddress = 0x0020;
    private const uint StunMagicCookie = 0x2112A442;

    private readonly object _syncLock = new();
    private UdpClient? _listener;
    private bool _disposed;

    /// <inheritdoc/>
    public event EventHandler<OnlineConnectionQuality>? ConnectionStatusChanged;

    /// <inheritdoc/>
    public OnlineConnectionQuality CurrentQuality { get; private set; } = OnlineConnectionQuality.Unknown;

    /// <inheritdoc/>
    public Task<OperationResult<IPEndPoint>> StartListeningAsync(int port, CancellationToken cancellationToken = default)
    {
        if (port is < 0 or > 65535)
        {
            return Task.FromResult(OperationResult<IPEndPoint>.CreateFailure("Port is out of range."));
        }

        lock (_syncLock)
        {
            ThrowIfDisposed();
            try
            {
                _listener?.Close();
                _listener = new UdpClient(port);
                var endpoint = (IPEndPoint)_listener.Client.LocalEndPoint!;
                SetQuality(OnlineConnectionQuality.Connecting);
                return Task.FromResult(OperationResult<IPEndPoint>.CreateSuccess(endpoint));
            }
            catch (SocketException ex)
            {
                logger.LogWarning(ex, "Failed to bind P2P listener on port {Port}.", port);
                return Task.FromResult(OperationResult<IPEndPoint>.CreateFailure("Failed to bind local port."));
            }
        }
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
            var punch = new byte[] { 0x47, 0x48, 0x50, 0x31 };
            for (var i = 0; i < PunchPacketCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await listener.SendAsync(punch, target, cancellationToken);
            }

            SetQuality(OnlineConnectionQuality.Direct);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException)
        {
            throw;
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is SocketException or TimeoutException)
        {
            logger.LogWarning("STUN query failed: {Message}", OnlineLogScrubber.Scrub(ex.Message));
            return OperationResult<P2PEndpoints>.CreateSuccess(new P2PEndpoints(local, null));
        }
    }

    /// <inheritdoc/>
    public Task<OperationResult<bool>> StopListeningAsync(CancellationToken cancellationToken = default)
    {
        lock (_syncLock)
        {
            _listener?.Close();
            _listener = null;
            SetQuality(OnlineConnectionQuality.Unknown);
            return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
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
            _listener?.Close();
            _listener = null;
        }
    }

    private static byte[] BuildBindingRequest(out byte[] transactionId)
    {
        transactionId = new byte[12];
        Random.Shared.NextBytes(transactionId);

        var request = new byte[20];
        request[0] = (byte)(StunBindingRequest >> 8);
        request[1] = (byte)StunBindingRequest;
        request[4] = (byte)((StunMagicCookie >> 24) & 0xFF);
        request[5] = (byte)((StunMagicCookie >> 16) & 0xFF);
        request[6] = (byte)((StunMagicCookie >> 8) & 0xFF);
        request[7] = (byte)(StunMagicCookie & 0xFF);
        Buffer.BlockCopy(transactionId, 0, request, 8, 12);
        return request;
    }

    private static IPEndPoint? ParseBindingResponse(byte[] response, byte[] transactionId)
    {
        if (response.Length < 20 || response[0] != ((StunBindingResponse >> 8) & 0xFF) || response[1] != (StunBindingResponse & 0xFF))
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
            if (type == StunXorMappedAddress && offset + 4 + length <= response.Length)
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

        var port = (ushort)(((buffer[offset + 2] << 8) | buffer[offset + 3]) ^ (StunMagicCookie >> 16));
        var address = new byte[4];
        for (var i = 0; i < 4; i++)
        {
            address[i] = (byte)(buffer[offset + 4 + i] ^ (StunMagicCookie >> (24 - (i * 8))));
        }

        return new IPEndPoint(new IPAddress(address), port);
    }

    private async Task<IPEndPoint?> QueryStunAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(StunTimeoutSeconds));

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(ApiConstants.OnlineStunHost, timeoutCts.Token);
            if (addresses.Length == 0)
            {
                return null;
            }

            using var udp = new UdpClient(0);
            var request = BuildBindingRequest(out var transactionId);
            await udp.SendAsync(request, new IPEndPoint(addresses[0], StunPort), timeoutCts.Token);

            var result = await udp.ReceiveAsync(timeoutCts.Token);
            return ParseBindingResponse(result.Buffer, transactionId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private void SetQuality(OnlineConnectionQuality quality)
    {
        CurrentQuality = quality;
        ConnectionStatusChanged?.Invoke(this, quality);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
