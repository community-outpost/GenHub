using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Online;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Services.Online;

/// <summary>
/// In-process virtual LAN tunnel runner handling Zero Hour discovery and game datagram routing over the relay.
/// </summary>
public sealed class VirtualLanTunnelRunner(ILogger<VirtualLanTunnelRunner> logger) : ITunnelRunner
{
    private const int DefaultRelayPort = 8088;
    private const int ZeroHourDiscoveryPort = 8086;
    private const int ZeroHourGamePort = 16000;
    private const int KeepAliveIntervalSeconds = 15;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private CancellationTokenSource? _cts;
    private UdpClient? _relayClient;
    private UdpClient? _broadcastListener;
    private Task? _relayReceiveTask;
    private Task? _broadcastReceiveTask;
    private Task? _keepAliveTask;
    private bool _disposed;

    /// <inheritdoc/>
    public bool IsRunning { get; private set; }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> StartAsync(
        string adapterConfig,
        string overlayIp,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning)
            {
                return OperationResult<bool>.CreateSuccess(true);
            }

            if (!TryParseConfig(adapterConfig, overlayIp, out var parsed))
            {
                logger.LogWarning("Failed to parse virtual LAN tunnel adapter config.");
                return OperationResult<bool>.CreateFailure("Invalid adapter configuration.");
            }

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            // Initialize relay UDP client
            _relayClient = new UdpClient(AddressFamily.InterNetwork);
            _relayClient.Connect(parsed.RelayEndpoint);

            // Send registration keep-alive to bind relay mapping
            SendRegistrationPing(parsed);

            // Start background loops
            _relayReceiveTask = Task.Run(() => RelayReceiveLoopAsync(_relayClient, parsed, ct), ct);
            _keepAliveTask = Task.Run(() => KeepAliveLoopAsync(parsed, ct), ct);

            // Attempt to bind Zero Hour discovery listener (UDP 8086)
            try
            {
                _broadcastListener = new UdpClient();
                _broadcastListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _broadcastListener.Client.Bind(new IPEndPoint(IPAddress.Any, ZeroHourDiscoveryPort));
                _broadcastReceiveTask = Task.Run(() => BroadcastReceiveLoopAsync(_broadcastListener, parsed, ct), ct);
            }
            catch (SocketException ex)
            {
                logger.LogInformation("Zero Hour discovery port {Port} shared or already in use: {Message}", ZeroHourDiscoveryPort, ex.Message);
            }

            IsRunning = true;
            logger.LogInformation(
                "Virtual LAN tunnel runner started for network {NetworkId} (Overlay IP: {OverlayIp}, Relay: {RelayEndpoint}).",
                parsed.NetworkId,
                parsed.OverlayIp,
                parsed.RelayEndpoint);

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start virtual LAN tunnel runner.");
            await StopInternalAsync();
            return OperationResult<bool>.CreateFailure($"Tunnel runner start failed: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> StopAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await StopInternalAsync();
            return OperationResult<bool>.CreateSuccess(true);
        }
        finally
        {
            _lock.Release();
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
        StopInternalAsync().GetAwaiter().GetResult();
        _lock.Dispose();
    }

    private static bool TryParseConfig(string adapterConfig, string overlayIp, out ParsedTunnelConfig config)
    {
        config = default!;
        try
        {
            var json = adapterConfig;
            try
            {
                var rawBytes = Convert.FromBase64String(adapterConfig);
                json = Encoding.UTF8.GetString(rawBytes);
            }
            catch (FormatException)
            {
                // Assume raw JSON
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var networkIdStr = root.TryGetProperty("networkId", out var netProp) ? netProp.GetString() ?? string.Empty : string.Empty;
            var netBytes = new byte[16];
            if (Guid.TryParse(networkIdStr, out var netGuid))
            {
                var guidBytes = netGuid.ToByteArray();
                Buffer.BlockCopy(guidBytes, 0, netBytes, 0, 16);
            }
            else
            {
                var rawStrBytes = Encoding.UTF8.GetBytes(networkIdStr);
                var copyLen = Math.Min(rawStrBytes.Length, 16);
                Buffer.BlockCopy(rawStrBytes, 0, netBytes, 0, copyLen);
            }

            var ipStr = overlayIp;
            if (string.IsNullOrWhiteSpace(ipStr) && root.TryGetProperty("overlayIp", out var ipProp))
            {
                ipStr = ipProp.GetString() ?? string.Empty;
            }

            if (!IPAddress.TryParse(ipStr, out var parsedIp))
            {
                parsedIp = IPAddress.Parse("10.42.0.2");
            }

            var relayHost = "152.70.171.121";
            var relayPort = DefaultRelayPort;

            if (root.TryGetProperty("relay", out var relayProp) && relayProp.ValueKind == JsonValueKind.Object)
            {
                if (relayProp.TryGetProperty("host", out var hostProp) && !string.IsNullOrWhiteSpace(hostProp.GetString()))
                {
                    relayHost = hostProp.GetString()!;
                }

                if (relayProp.TryGetProperty("port", out var portProp) && portProp.TryGetInt32(out var p))
                {
                    relayPort = p;
                }
            }

            var ep = new IPEndPoint(IPAddress.Parse(relayHost), relayPort);
            config = new ParsedTunnelConfig(networkIdStr, netBytes, parsedIp, parsedIp.GetAddressBytes(), ep);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task StopInternalAsync()
    {
        if (!IsRunning && _cts == null)
        {
            return;
        }

        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already canceled
        }

        _relayClient?.Dispose();
        _relayClient = null;

        _broadcastListener?.Dispose();
        _broadcastListener = null;

        if (_relayReceiveTask != null)
        {
            try
            {
                await _relayReceiveTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Ignored on shutdown
            }

            _relayReceiveTask = null;
        }

        if (_broadcastReceiveTask != null)
        {
            try
            {
                await _broadcastReceiveTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Ignored on shutdown
            }

            _broadcastReceiveTask = null;
        }

        if (_keepAliveTask != null)
        {
            try
            {
                await _keepAliveTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Ignored on shutdown
            }

            _keepAliveTask = null;
        }

        _cts?.Dispose();
        _cts = null;

        IsRunning = false;
        logger.LogInformation("Virtual LAN tunnel runner stopped.");
    }

    private void SendRegistrationPing(ParsedTunnelConfig config)
    {
        try
        {
            var packet = new byte[24];
            Buffer.BlockCopy(config.NetworkIdBytes, 0, packet, 0, 16);

            // Bytes 16..19: 0.0.0.0 (Target: registration)
            // Bytes 20..23: Local overlay IP
            Buffer.BlockCopy(config.OverlayIpBytes, 0, packet, 20, 4);
            _relayClient?.Send(packet, packet.Length);
        }
        catch (Exception ex)
        {
            logger.LogDebug("Registration ping failed: {Message}", ex.Message);
        }
    }

    private async Task KeepAliveLoopAsync(ParsedTunnelConfig config, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(KeepAliveIntervalSeconds));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                {
                    break;
                }

                SendRegistrationPing(config);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogDebug("Keep-alive error: {Message}", ex.Message);
            }
        }
    }

    private async Task RelayReceiveLoopAsync(UdpClient client, ParsedTunnelConfig config, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await client.ReceiveAsync(ct).ConfigureAwait(false);
                var data = result.Buffer;
                if (data.Length < 24)
                {
                    continue;
                }

                // Verify network ID matches
                var matchesNetwork = true;
                for (var i = 0; i < 16; i++)
                {
                    if (data[i] != config.NetworkIdBytes[i])
                    {
                        matchesNetwork = false;
                        break;
                    }
                }

                if (!matchesNetwork)
                {
                    continue;
                }

                // Parse target IP and payload
                var isBroadcast = data[16] == 255 || (data[16] == 10 && data[17] == 42 && (data[18] == 255 || data[19] == 255));
                var isUnicastToMe = data[16] == config.OverlayIpBytes[0] &&
                                    data[17] == config.OverlayIpBytes[1] &&
                                    data[18] == config.OverlayIpBytes[2] &&
                                    data[19] == config.OverlayIpBytes[3];

                if (!isBroadcast && !isUnicastToMe)
                {
                    continue;
                }

                var payloadLength = data.Length - 24;
                if (payloadLength <= 0)
                {
                    continue;
                }

                // Forward payload locally to Zero Hour discovery or game port
                var targetPort = isBroadcast ? ZeroHourDiscoveryPort : ZeroHourGamePort;
                try
                {
                    using var localSender = new UdpClient();
                    localSender.EnableBroadcast = isBroadcast;
                    var localTarget = isBroadcast
                        ? new IPEndPoint(IPAddress.Loopback, targetPort)
                        : new IPEndPoint(IPAddress.Loopback, targetPort);

                    var payload = new byte[payloadLength];
                    Buffer.BlockCopy(data, 24, payload, 0, payloadLength);
                    await localSender.SendAsync(payload, payload.Length, localTarget).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogDebug("Local dispatch error: {Message}", ex.Message);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogDebug("Relay receive loop error: {Message}", ex.Message);
            }
        }
    }

    private async Task BroadcastReceiveLoopAsync(UdpClient listener, ParsedTunnelConfig config, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await listener.ReceiveAsync(ct).ConfigureAwait(false);
                var payload = result.Buffer;
                if (payload.Length == 0)
                {
                    continue;
                }

                // Frame packet for relay fan-out:
                // [0..15] NetworkId
                // [16..19] Broadcast IP (255.255.255.255)
                // [20..23] Source Overlay IP
                // [24..] Payload
                var framed = new byte[24 + payload.Length];
                Buffer.BlockCopy(config.NetworkIdBytes, 0, framed, 0, 16);
                framed[16] = 255;
                framed[17] = 255;
                framed[18] = 255;
                framed[19] = 255;
                Buffer.BlockCopy(config.OverlayIpBytes, 0, framed, 20, 4);
                Buffer.BlockCopy(payload, 0, framed, 24, payload.Length);

                if (_relayClient != null)
                {
                    await _relayClient.SendAsync(framed, framed.Length).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogDebug("Broadcast listener loop error: {Message}", ex.Message);
            }
        }
    }

    private sealed record ParsedTunnelConfig(
        string NetworkId,
        byte[] NetworkIdBytes,
        IPAddress OverlayIp,
        byte[] OverlayIpBytes,
        IPEndPoint RelayEndpoint);
}
