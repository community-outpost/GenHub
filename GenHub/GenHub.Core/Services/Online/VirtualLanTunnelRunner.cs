using GenHub.Core.Interfaces.Online;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Services.Online;

/// <summary>
/// In-process virtual LAN tunnel runner that bridges Zero Hour LAN traffic
/// across a UDP relay server, masking client IP addresses.
/// </summary>
public sealed class VirtualLanTunnelRunner(ILogger<VirtualLanTunnelRunner> logger) : ITunnelRunner
{
    private const int ZeroHourDiscoveryPort = 8086;
    private const int ZeroHourGamePort = 16000;
    private const int DefaultRelayPort = 8088;
    private const int KeepAliveIntervalSeconds = 15;
    private const string DefaultRelayHost = "152.70.171.121"; // NOSONAR: Default private relay node fallback
    private const string DefaultOverlayFallbackIp = "10.42.0.2"; // NOSONAR: Default virtual LAN IP fallback

    private readonly SemaphoreSlim _lock = new(1, 1);
    private UdpClient? _relayClient;
    private UdpClient? _broadcastListener;
    private CancellationTokenSource? _cts;
    private Task? _relayReceiveTask;
    private Task? _broadcastReceiveTask;
    private Task? _keepAliveTask;
    private bool _disposed;

    /// <inheritdoc/>
    public bool IsRunning { get; private set; }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> StartAsync(string adapterConfig, string overlayIp, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (IsRunning)
            {
                return OperationResult<bool>.CreateSuccess(true);
            }

            if (!TryParseConfig(adapterConfig, overlayIp, out var parsed))
            {
                logger.LogWarning("Failed to parse adapter config for virtual LAN tunnel runner.");
                return OperationResult<bool>.CreateFailure("Invalid adapter configuration.");
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var ct = _cts.Token;

            _relayClient = new UdpClient();
            _relayClient.Connect(parsed.RelayEndpoint);

            SendRegistrationPing(parsed);

            _relayReceiveTask = Task.Run(() => RelayReceiveLoopAsync(_relayClient, parsed, ct), ct);
            _keepAliveTask = Task.Run(() => KeepAliveLoopAsync(parsed, ct), ct);

            try
            {
                _broadcastListener = new UdpClient();
                _broadcastListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _broadcastListener.Client.Bind(new IPEndPoint(IPAddress.Any, ZeroHourDiscoveryPort));
                _broadcastReceiveTask = Task.Run(() => BroadcastReceiveLoopAsync(_broadcastListener, parsed, ct), ct);
            }
            catch (SocketException ex)
            {
                logger.LogInformation(ex, "Zero Hour discovery port {Port} shared or already in use: {Message}", ZeroHourDiscoveryPort, ex.Message);
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
            logger.LogError(ex, "Failed to start virtual LAN tunnel runner: {Message}", ex.Message);
            await StopInternalAsync().ConfigureAwait(false);
            return OperationResult<bool>.CreateFailure($"Failed to start virtual LAN tunnel runner: {ex.Message}");
        }
        finally
        {
            try
            {
                _lock.Release();
            }
            catch (ObjectDisposedException)
            {
                // Disposed concurrently
            }
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> StopAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            await StopInternalAsync().ConfigureAwait(false);
            return OperationResult<bool>.CreateSuccess(true);
        }
        finally
        {
            try
            {
                _lock.Release();
            }
            catch (ObjectDisposedException)
            {
                // Disposed concurrently
            }
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
        try
        {
            if (_lock.Wait(TimeSpan.FromSeconds(5), CancellationToken.None))
            {
                try
                {
                    StopInternalAsync().GetAwaiter().GetResult();
                }
                finally
                {
                    _lock.Release();
                }
            }
            else
            {
                StopInternalAsync().GetAwaiter().GetResult();
            }
        }
        catch (ObjectDisposedException)
        {
            // Already disposed
        }
        finally
        {
            _lock.Dispose();
        }
    }

    private static bool TryParseConfig(string adapterConfig, string overlayIp, out ParsedTunnelConfig config)
    {
        config = default!;
        try
        {
            var json = ExtractJson(adapterConfig);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var networkIdStr = root.TryGetProperty("networkId", out var netProp) && netProp.ValueKind == JsonValueKind.String
                ? netProp.GetString() ?? string.Empty
                : string.Empty;
            var netBytes = ParseNetworkIdBytes(networkIdStr);

            var ipStr = overlayIp;
            if (string.IsNullOrWhiteSpace(ipStr) && root.TryGetProperty("overlayIp", out var ipProp) && ipProp.ValueKind == JsonValueKind.String)
            {
                ipStr = ipProp.GetString() ?? string.Empty;
            }

            if (!IPAddress.TryParse(ipStr, out var parsedIp))
            {
                parsedIp = IPAddress.Parse(DefaultOverlayFallbackIp);
            }

            var ep = ParseRelayEndpoint(root);
            config = new ParsedTunnelConfig(networkIdStr, netBytes, parsedIp, parsedIp.GetAddressBytes(), ep);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or FormatException or InvalidOperationException)
        {
            return false;
        }
    }

    private static string ExtractJson(string adapterConfig)
    {
        try
        {
            var rawBytes = Convert.FromBase64String(adapterConfig);
            return Encoding.UTF8.GetString(rawBytes);
        }
        catch (FormatException)
        {
            return adapterConfig;
        }
    }

    private static byte[] ParseNetworkIdBytes(string networkIdStr)
    {
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

        return netBytes;
    }

    private static IPEndPoint ParseRelayEndpoint(JsonElement root)
    {
        var relayHost = DefaultRelayHost;
        var relayPort = DefaultRelayPort;

        if (root.TryGetProperty("relay", out var relayProp) && relayProp.ValueKind == JsonValueKind.Object)
        {
            if (relayProp.TryGetProperty("host", out var hostProp) && hostProp.ValueKind == JsonValueKind.String)
            {
                var hostStr = hostProp.GetString();
                if (!string.IsNullOrWhiteSpace(hostStr))
                {
                    relayHost = hostStr;
                }
            }

            if (relayProp.TryGetProperty("port", out var portProp) && portProp.TryGetInt32(out var p))
            {
                relayPort = p;
            }
        }

        if (!IPAddress.TryParse(relayHost, out var ip))
        {
            try
            {
                var addresses = Dns.GetHostAddresses(relayHost);
                ip = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                    ?? addresses.FirstOrDefault()
                    ?? throw new FormatException($"Cannot resolve host {relayHost}");
            }
            catch (SocketException ex)
            {
                throw new FormatException($"Failed to resolve relay host {relayHost}", ex);
            }
        }

        return new IPEndPoint(ip, relayPort);
    }

    private static bool IsValidRelayPacket(byte[] data, ParsedTunnelConfig config, out bool isBroadcast, out int payloadLength)
    {
        isBroadcast = false;
        payloadLength = 0;

        if (data.Length < 24)
        {
            return false;
        }

        for (var i = 0; i < 16; i++)
        {
            if (data[i] != config.NetworkIdBytes[i])
            {
                return false;
            }
        }

        isBroadcast = data[16] == 255 || (data[16] == 10 && data[17] == 42 && (data[18] == 255 || data[19] == 255));
        var isUnicastToMe = data[16] == config.OverlayIpBytes[0] &&
                            data[17] == config.OverlayIpBytes[1] &&
                            data[18] == config.OverlayIpBytes[2] &&
                            data[19] == config.OverlayIpBytes[3];

        if (!isBroadcast && !isUnicastToMe)
        {
            return false;
        }

        payloadLength = data.Length - 24;
        return payloadLength > 0;
    }

    private static async Task CancelTokenSourceSilentlyAsync(CancellationTokenSource? cts)
    {
        if (cts == null)
        {
            return;
        }

        try
        {
            await cts.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Already canceled
        }
    }

    private static async Task AwaitCancellationSilentlyAsync(Task? task)
    {
        if (task == null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // Task canceled cleanly
        }
    }

    private async Task StopInternalAsync()
    {
        if (!IsRunning && _cts == null)
        {
            return;
        }

        await CancelTokenSourceSilentlyAsync(_cts).ConfigureAwait(false);

        _relayClient?.Dispose();
        _relayClient = null;

        _broadcastListener?.Dispose();
        _broadcastListener = null;

        await AwaitCancellationSilentlyAsync(_relayReceiveTask).ConfigureAwait(false);
        _relayReceiveTask = null;

        await AwaitCancellationSilentlyAsync(_broadcastReceiveTask).ConfigureAwait(false);
        _broadcastReceiveTask = null;

        await AwaitCancellationSilentlyAsync(_keepAliveTask).ConfigureAwait(false);
        _keepAliveTask = null;

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
            Buffer.BlockCopy(config.OverlayIpBytes, 0, packet, 20, 4);
            _relayClient?.Send(packet, packet.Length);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Registration ping failed: {Message}", ex.Message);
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
                logger.LogDebug(ex, "Keep-alive error: {Message}", ex.Message);
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
                if (!IsValidRelayPacket(data, config, out var isBroadcast, out var payloadLength))
                {
                    continue;
                }

                await DispatchLocalPacketAsync(data, payloadLength, isBroadcast).ConfigureAwait(false);
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
                logger.LogDebug(ex, "Relay receive loop error: {Message}", ex.Message);
            }
        }
    }

    private async Task DispatchLocalPacketAsync(byte[] data, int payloadLength, bool isBroadcast)
    {
        var targetPort = isBroadcast ? ZeroHourDiscoveryPort : ZeroHourGamePort;
        try
        {
            using var localSender = new UdpClient();
            localSender.EnableBroadcast = isBroadcast;
            var localTarget = new IPEndPoint(IPAddress.Loopback, targetPort);

            var payload = new byte[payloadLength];
            Buffer.BlockCopy(data, 24, payload, 0, payloadLength);
            await localSender.SendAsync(payload, payload.Length, localTarget).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Local dispatch error: {Message}", ex.Message);
        }
    }

    private async Task BroadcastReceiveLoopAsync(UdpClient listener, ParsedTunnelConfig config, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await listener.ReceiveAsync(ct).ConfigureAwait(false);
                var data = result.Buffer;
                if (data.Length == 0 || _relayClient == null)
                {
                    continue;
                }

                // Packet header: 16 bytes netId + 4 bytes destIp (255.255.255.255) + 4 bytes srcIp + payload
                var packet = new byte[24 + data.Length];
                Buffer.BlockCopy(config.NetworkIdBytes, 0, packet, 0, 16);
                packet[16] = 255;
                packet[17] = 255;
                packet[18] = 255;
                packet[19] = 255;
                Buffer.BlockCopy(config.OverlayIpBytes, 0, packet, 20, 4);
                Buffer.BlockCopy(data, 0, packet, 24, data.Length);

                await _relayClient.SendAsync(packet, packet.Length).ConfigureAwait(false);
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
                logger.LogDebug(ex, "Broadcast receive loop error: {Message}", ex.Message);
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
