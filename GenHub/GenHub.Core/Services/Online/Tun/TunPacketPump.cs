using GenHub.Core.Interfaces.Online;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Services.Online.Tun;

/// <summary>
/// Pumps raw IPv4 packets between a local <see cref="ITunDevice"/> and the remote UDP packet relay server.
/// </summary>
public sealed class TunPacketPump : IDisposable
{
    private const int RelayHeaderLength = 24;
    private const int MinIpv4HeaderLength = 20;
    private const int KeepAliveIntervalSeconds = 15;

    private readonly ITunDevice _device;
    private readonly IPEndPoint _relayEndpoint;
    private readonly byte[] _networkIdBytes;
    private readonly IPAddress _overlayIp;
    private readonly ILogger _logger;
    private readonly UdpClient _udpClient;

    private CancellationTokenSource? _pumpCts;
    private Task? _outboundTask;
    private Task? _inboundTask;
    private Task? _keepAliveTask;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TunPacketPump"/> class.
    /// </summary>
    /// <param name="device">The local TUN device.</param>
    /// <param name="relayEndpoint">The remote UDP relay server endpoint.</param>
    /// <param name="networkId">The network identifier string or GUID.</param>
    /// <param name="overlayIp">The local overlay IPv4 address.</param>
    /// <param name="logger">Optional logger.</param>
    public TunPacketPump(
        ITunDevice device,
        IPEndPoint relayEndpoint,
        string networkId,
        IPAddress overlayIp,
        ILogger? logger = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _relayEndpoint = relayEndpoint ?? throw new ArgumentNullException(nameof(relayEndpoint));
        _overlayIp = overlayIp ?? throw new ArgumentNullException(nameof(overlayIp));
        _logger = logger ?? NullLogger.Instance;

        _networkIdBytes = ParseNetworkIdBytes(networkId);
        _udpClient = new UdpClient();
        _udpClient.Connect(_relayEndpoint);
    }

    /// <summary>
    /// Starts the packet pump processing tasks.
    /// </summary>
    public void Start()
    {
        if (_pumpCts != null)
        {
            return;
        }

        _pumpCts = new CancellationTokenSource();
        var token = _pumpCts.Token;

        _outboundTask = Task.Run(() => OutboundLoopAsync(token), token);
        _inboundTask = Task.Run(() => InboundLoopAsync(token), token);
        _keepAliveTask = Task.Run(() => KeepAliveLoopAsync(token), token);

        _logger.LogInformation("TUN packet pump started for {OverlayIp} -> {RelayEndpoint}", _overlayIp, _relayEndpoint);
    }

    /// <summary>
    /// Stops the packet pump processing tasks asynchronously.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task StopAsync()
    {
        if (_pumpCts == null)
        {
            return;
        }

        try
        {
            _pumpCts.Cancel();

            var tasks = new List<Task>();
            if (_outboundTask != null)
            {
                tasks.Add(_outboundTask);
            }

            if (_inboundTask != null)
            {
                tasks.Add(_inboundTask);
            }

            if (_keepAliveTask != null)
            {
                tasks.Add(_keepAliveTask);
            }

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Exception during TUN packet pump stop");
        }
        finally
        {
            _pumpCts.Dispose();
            _pumpCts = null;
        }

        _logger.LogInformation("TUN packet pump stopped.");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _pumpCts?.Cancel();
            _udpClient.Dispose();
            _pumpCts?.Dispose();
        }
        catch
        {
            // Best effort disposal
        }
    }

    private static byte[] ParseNetworkIdBytes(string networkId)
    {
        var bytes = new byte[16];

        // Clean hex (handles both GUID with hyphens and raw 32-char hex in big-endian network order)
        var cleanHex = networkId.Replace("-", string.Empty);
        if (cleanHex.Length >= 32)
        {
            try
            {
                return Convert.FromHexString(cleanHex[..32]);
            }
            catch
            {
                // Fallback to UTF8 bytes
            }
        }

        var utf8 = System.Text.Encoding.UTF8.GetBytes(networkId);
        Buffer.BlockCopy(utf8, 0, bytes, 0, Math.Min(16, utf8.Length));
        return bytes;
    }

    private async Task OutboundLoopAsync(CancellationToken cancellationToken)
    {
        var readBuffer = new byte[65535];
        var overlayBytes = _overlayIp.GetAddressBytes();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var length = await _device.ReadPacketAsync(readBuffer, cancellationToken).ConfigureAwait(false);
                if (length < MinIpv4HeaderLength)
                {
                    continue;
                }

                // Verify IPv4 header (version 4)
                if ((readBuffer[0] >> 4) != 4)
                {
                    continue;
                }

                var destB0 = readBuffer[16];
                var destB1 = readBuffer[17];
                var destB2 = readBuffer[18];
                var destB3 = readBuffer[19];

                // Detect broadcast (255.255.255.255 or 10.42.15.255 / 10.42.255.255)
                var isBroadcast = destB0 == 255 ||
                    (destB0 == 10 && destB1 == 42 && (destB2 == 15 || destB2 == 255 || destB3 == 255));

                var frame = new byte[RelayHeaderLength + length];
                Buffer.BlockCopy(_networkIdBytes, 0, frame, 0, 16);

                if (isBroadcast)
                {
                    frame[16] = 255;
                    frame[17] = 255;
                    frame[18] = 255;
                    frame[19] = 255;
                }
                else
                {
                    frame[16] = destB0;
                    frame[17] = destB1;
                    frame[18] = destB2;
                    frame[19] = destB3;
                }

                Buffer.BlockCopy(overlayBytes, 0, frame, 20, 4);
                Buffer.BlockCopy(readBuffer, 0, frame, RelayHeaderLength, length);

                await _udpClient.SendAsync(frame, frame.Length).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogTrace(ex, "Error reading from TUN device or sending to relay");
                }
            }
        }
    }

    private async Task InboundLoopAsync(CancellationToken cancellationToken)
    {
        var overlayBytes = _overlayIp.GetAddressBytes();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                var data = result.Buffer;
                if (data.Length < RelayHeaderLength + MinIpv4HeaderLength)
                {
                    continue;
                }

                // Verify NetworkId matches
                var matchNet = true;
                for (var i = 0; i < 16; i++)
                {
                    if (data[i] != _networkIdBytes[i])
                    {
                        matchNet = false;
                        break;
                    }
                }

                if (!matchNet)
                {
                    continue;
                }

                // Verify target IP is broadcast or our overlay IP
                var targetB0 = data[16];
                var targetB1 = data[17];
                var targetB2 = data[18];
                var targetB3 = data[19];

                var isBroadcast = targetB0 == 255 ||
                    (targetB0 == 10 && targetB1 == 42 && (targetB2 == 15 || targetB2 == 255 || targetB3 == 255));

                var isForUs = isBroadcast ||
                    (targetB0 == overlayBytes[0] &&
                     targetB1 == overlayBytes[1] &&
                     targetB2 == overlayBytes[2] &&
                     targetB3 == overlayBytes[3]);

                if (!isForUs)
                {
                    continue;
                }

                // Extract raw IPv4 packet and inject into TUN device
                var ipPacket = data.AsMemory(RelayHeaderLength);
                await _device.WritePacketAsync(ipPacket, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogTrace(ex, "Error receiving from relay or writing to TUN device");
                }
            }
        }
    }

    private async Task KeepAliveLoopAsync(CancellationToken cancellationToken)
    {
        var pingFrame = new byte[RelayHeaderLength];
        Buffer.BlockCopy(_networkIdBytes, 0, pingFrame, 0, 16);

        // Target: 0.0.0.0 (indicates registration / ping to relay)
        pingFrame[16] = 0;
        pingFrame[17] = 0;
        pingFrame[18] = 0;
        pingFrame[19] = 0;

        // Source: Our Overlay IP
        var overlayBytes = _overlayIp.GetAddressBytes();
        Buffer.BlockCopy(overlayBytes, 0, pingFrame, 20, 4);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _udpClient.SendAsync(pingFrame, pingFrame.Length).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(KeepAliveIntervalSeconds), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogTrace(ex, "Error sending keepalive ping to relay");
                }
            }
        }
    }
}
