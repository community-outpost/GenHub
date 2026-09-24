using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Online;
using GenHub.Core.Models.Results;
using GenHub.Core.Services.Online.Tun;
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
/// across a UDP relay server, using a real TUN virtual adapter when available,
/// or fallback UDP proxying.
/// </summary>
public sealed class VirtualLanTunnelRunner(ILogger<VirtualLanTunnelRunner> logger) : ITunnelRunner
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ITunDevice? _tunDevice;
    private TunPacketPump? _tunPump;
    private UdpClient? _relayClient;
    private UdpClient? _broadcastListener;
    private UdpClient? _localSender;
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

        var (parseSuccess, parsed) = await TryParseConfigAsync(adapterConfig, overlayIp, cancellationToken).ConfigureAwait(false);
        if (!parseSuccess)
        {
            logger.LogWarning("Failed to parse adapter config for virtual LAN tunnel runner.");
            return OperationResult<bool>.CreateFailure("Invalid adapter configuration.");
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (IsRunning)
            {
                return OperationResult<bool>.CreateSuccess(true);
            }

            // Attempt to bring up a real layer-3 TUN adapter first
            if (OperatingSystem.IsWindows())
            {
                var winResult = WindowsTunDevice.CreateOrOpen(
                    OnlineConstants.TunDefaultWindowsInterfaceName,
                    parsed.OverlayIp,
                    parsed.PrefixLength,
                    OnlineConstants.TunDefaultMtu);

                if (winResult.Success && winResult.Data != null)
                {
                    _tunDevice = winResult.Data;
                    _tunPump = new TunPacketPump(
                        _tunDevice,
                        parsed.RelayEndpoint,
                        parsed.NetworkId,
                        parsed.OverlayIp,
                        logger,
                        parsed.PrefixLength);
                    _tunPump.Start();

                    IsRunning = true;
                    logger.LogInformation(
                        "Virtual LAN Wintun adapter {Interface} active with IP {Ip} via relay {Relay}.",
                        _tunDevice.InterfaceName,
                        parsed.OverlayIp,
                        parsed.RelayEndpoint);

                    return OperationResult<bool>.CreateSuccess(true);
                }

                logger.LogError(
                    "Wintun adapter unavailable: {Error}",
                    winResult.AllErrors);

                return OperationResult<bool>.CreateFailure(
                    $"Virtual LAN network adapter unavailable: {winResult.FirstError}");
            }
            else if (OperatingSystem.IsLinux() && LinuxTunInterface.Exists(OnlineConstants.TunDefaultInterfaceName))
            {
                var linuxResult = LinuxTunDevice.Attach(OnlineConstants.TunDefaultInterfaceName, parsed.OverlayIp);
                if (linuxResult.Success && linuxResult.Data != null)
                {
                    _tunDevice = linuxResult.Data;
                    _tunPump = new TunPacketPump(
                        _tunDevice,
                        parsed.RelayEndpoint,
                        parsed.NetworkId,
                        parsed.OverlayIp,
                        logger,
                        parsed.PrefixLength);
                    _tunPump.Start();

                    IsRunning = true;
                    logger.LogInformation(
                        "Virtual LAN Linux TUN adapter {Interface} active with IP {Ip} via relay {Relay}.",
                        _tunDevice.InterfaceName,
                        parsed.OverlayIp,
                        parsed.RelayEndpoint);

                    return OperationResult<bool>.CreateSuccess(true);
                }
            }

            // Fallback: in-process UDP socket proxy
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
                EnableReusePort(_broadcastListener.Client, logger);
                _broadcastListener.Client.Bind(new IPEndPoint(IPAddress.Any, OnlineConstants.ZeroHourDiscoveryPort));
                _broadcastReceiveTask = Task.Run(() => BroadcastReceiveLoopAsync(_broadcastListener, parsed, ct), ct);
            }
            catch (SocketException ex)
            {
                logger.LogInformation(ex, "Zero Hour discovery port {Port} shared or already in use: {Message}", OnlineConstants.ZeroHourDiscoveryPort, ex.Message);
            }

            ObjectDisposedException.ThrowIf(_disposed, this);

            IsRunning = true;
            logger.LogInformation(
                "Virtual LAN socket proxy started for network {NetworkId} (Overlay IP: {OverlayIp}, Relay: {RelayEndpoint}).",
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
                logger.LogWarning("Virtual LAN tunnel runner disposal timed out waiting for the lock; stopping without it.");
                StopWithoutLock();
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

    /// <summary>
    /// Enables best-effort UDP port sharing on the discovery listener so cooperative tooling or test harnesses
    /// can bind the discovery port concurrently.
    /// </summary>
    /// <param name="socket">The discovery listener socket, not yet bound.</param>
    /// <param name="logger">The logger for the non-fatal fallback notice.</param>
    internal static void EnableReusePort(Socket socket, ILogger logger)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var (level, name) = OnlineConstants.GetReusePortOption();
            socket.SetRawSocketOption(level, name, BitConverter.GetBytes(1));
        }
        catch (Exception ex) when (ex is SocketException or PlatformNotSupportedException)
        {
            logger.LogDebug(ex, "SO_REUSEPORT unavailable; discovery port sharing stays disabled.");
        }
    }

    /// <summary>
    /// Builds a 24-byte registration or keepalive frame adhering to the relay contract:
    /// bytes 0..15: Network ID,
    /// bytes 16..19: Target IP (0.0.0.0 indicating registration or keepalive ping),
    /// bytes 20..23: Source IP (our overlay IP so relay keys membership by overlay IP).
    /// </summary>
    /// <param name="networkIdBytes">The 16-byte network identifier.</param>
    /// <param name="overlayIpBytes">The 4-byte overlay IPv4 address.</param>
    /// <returns>A 24-byte packet formatted for the relay keepalive/registration protocol.</returns>
    internal static byte[] BuildRegistrationPing(byte[] networkIdBytes, byte[] overlayIpBytes)
    {
        var ping = new byte[24];
        Buffer.BlockCopy(networkIdBytes, 0, ping, 0, 16);

        // Bytes 16..19 remain 0 (0.0.0.0 target)
        Buffer.BlockCopy(overlayIpBytes, 0, ping, 20, 4);
        return ping;
    }

    /// <summary>
    /// Evaluates whether a received discovery broadcast should be forwarded to the relay.
    /// Excludes loopback frames (runner self-echo from local re-injections) and own overlay IP frames.
    /// Passes game-originated discovery broadcasts sourced from local physical interfaces.
    /// </summary>
    /// <param name="sourceAddress">The source IP address of the received packet.</param>
    /// <param name="overlayIp">The local overlay IP address.</param>
    /// <returns><c>true</c> if the broadcast originated outside loopback/overlay and should be relayed; otherwise, <c>false</c>.</returns>
    internal static bool ShouldRelayBroadcast(IPAddress sourceAddress, IPAddress overlayIp)
    {
        return !sourceAddress.Equals(IPAddress.Loopback) && !sourceAddress.Equals(overlayIp);
    }

    private static async Task<(bool Success, ParsedTunnelConfig Config)> TryParseConfigAsync(
        string adapterConfig,
        string overlayIp,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = ExtractJson(adapterConfig);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (false, default!);
            }

            var networkIdStr = ExtractNetworkId(root);
            var netBytes = ParseNetworkIdBytes(networkIdStr);

            var ipStr = ExtractOverlayIpString(root, overlayIp);
            if (!IPAddress.TryParse(ipStr, out var parsedIp))
            {
                return (false, default!);
            }

            var prefixLength = ExtractPrefixLength(root);
            var ep = await ParseRelayEndpointAsync(root, cancellationToken).ConfigureAwait(false);
            var broadcastBytes = CalculateDirectedBroadcast(parsedIp, prefixLength);

            return (true, new ParsedTunnelConfig(
                networkIdStr, netBytes, parsedIp, parsedIp.GetAddressBytes(), ep, prefixLength, broadcastBytes));
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or FormatException or InvalidOperationException)
        {
            return (false, default!);
        }
    }

    private static string ExtractNetworkId(JsonElement root) =>
        root.TryGetProperty("networkId", out var netProp) && netProp.ValueKind == JsonValueKind.String
            ? netProp.GetString() ?? string.Empty
            : string.Empty;

    private static string ExtractOverlayIpString(JsonElement root, string overlayIp)
    {
        if (!string.IsNullOrWhiteSpace(overlayIp))
        {
            return overlayIp;
        }

        return root.TryGetProperty("overlayIp", out var ipProp) && ipProp.ValueKind == JsonValueKind.String
            ? ipProp.GetString() ?? string.Empty
            : string.Empty;
    }

    private static int ExtractPrefixLength(JsonElement root)
    {
        if (root.TryGetProperty("prefixLength", out var plProp) && plProp.TryGetInt32(out var pl))
        {
            return pl;
        }

        if (root.TryGetProperty("subnet", out var subnetProp) && subnetProp.ValueKind == JsonValueKind.String)
        {
            var subnet = subnetProp.GetString();
            if (!string.IsNullOrEmpty(subnet) && subnet.Contains('/'))
            {
                var slashIdx = subnet.IndexOf('/');
                if (int.TryParse(subnet[(slashIdx + 1)..], out var parsedPrefix))
                {
                    return parsedPrefix;
                }
            }
        }

        return OnlineConstants.TunOverlayPrefixLength;
    }

    private static byte[] CalculateDirectedBroadcast(IPAddress ip, int prefixLength)
    {
        var ipBytes = ip.GetAddressBytes();
        if (ipBytes.Length != 4)
        {
            return [255, 255, 255, 255];
        }

        if (prefixLength is < 0 or > 32)
        {
            prefixLength = 20;
        }

        uint ipNum = ((uint)ipBytes[0] << 24) | ((uint)ipBytes[1] << 16) | ((uint)ipBytes[2] << 8) | ipBytes[3];
        uint hostMask = prefixLength == 32 ? 0 : uint.MaxValue >> prefixLength;
        uint broadcastNum = ipNum | hostMask;

        return
        [
            (byte)((broadcastNum >> 24) & 0xFF),
            (byte)((broadcastNum >> 16) & 0xFF),
            (byte)((broadcastNum >> 8) & 0xFF),
            (byte)(broadcastNum & 0xFF),
        ];
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

    private static async Task<IPEndPoint> ParseRelayEndpointAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var (relayHost, relayPort) = ExtractRelayHostAndPort(root);

        if (!IPAddress.TryParse(relayHost, out var ip))
        {
            ip = await ResolveHostAddressAsync(relayHost, cancellationToken).ConfigureAwait(false);
        }

        return new IPEndPoint(ip, relayPort);
    }

    private static (string Host, int Port) ExtractRelayHostAndPort(JsonElement root)
    {
        var relayHost = ApiConstants.OnlineRelayHost;
        var relayPort = OnlineConstants.DefaultRelayPort;

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
        else
        {
            if (root.TryGetProperty("relayHost", out var rhProp) && rhProp.ValueKind == JsonValueKind.String)
            {
                var hostStr = rhProp.GetString();
                if (!string.IsNullOrWhiteSpace(hostStr))
                {
                    relayHost = hostStr;
                }
            }

            if (root.TryGetProperty("relayPort", out var rpProp) && rpProp.TryGetInt32(out var p))
            {
                relayPort = p;
            }
        }

        return (relayHost, relayPort);
    }

    private static async Task<IPAddress> ResolveHostAddressAsync(string relayHost, CancellationToken cancellationToken)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(relayHost, cancellationToken).ConfigureAwait(false);
            return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                ?? addresses.FirstOrDefault()
                ?? throw new FormatException($"Cannot resolve host {relayHost}");
        }
        catch (SocketException ex)
        {
            throw new FormatException($"Failed to resolve relay host {relayHost}", ex);
        }
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

        var isLimitedBroadcast = data[16] == 255 && data[17] == 255 && data[18] == 255 && data[19] == 255;
        var isDirectedBroadcast = data[16] == config.DirectedBroadcastBytes[0] &&
                                  data[17] == config.DirectedBroadcastBytes[1] &&
                                  data[18] == config.DirectedBroadcastBytes[2] &&
                                  data[19] == config.DirectedBroadcastBytes[3];
        isBroadcast = isLimitedBroadcast || isDirectedBroadcast;

        var isTargetMe = data[16] == config.OverlayIpBytes[0] &&
                         data[17] == config.OverlayIpBytes[1] &&
                         data[18] == config.OverlayIpBytes[2] &&
                         data[19] == config.OverlayIpBytes[3];

        if (!isBroadcast && !isTargetMe)
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

    private void StopWithoutLock()
    {
        if (_tunPump != null)
        {
            _tunPump.Dispose();
            _tunPump = null;
        }

        if (_tunDevice != null)
        {
            _tunDevice.Dispose();
            _tunDevice = null;
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

        _localSender?.Dispose();
        _localSender = null;

        _cts?.Dispose();
        _cts = null;

        IsRunning = false;
    }

    private async Task StopInternalAsync()
    {
        if (_tunPump != null)
        {
            await _tunPump.StopAsync().ConfigureAwait(false);
            _tunPump.Dispose();
            _tunPump = null;
        }

        if (_tunDevice != null)
        {
            _tunDevice.Dispose();
            _tunDevice = null;
        }

        if (!IsRunning && _cts == null)
        {
            return;
        }

        await CancelTokenSourceSilentlyAsync(_cts).ConfigureAwait(false);

        _localSender?.Dispose();
        _localSender = null;

        _broadcastListener?.Dispose();
        _broadcastListener = null;

        _relayClient?.Dispose();
        _relayClient = null;

        await AwaitCancellationSilentlyAsync(_relayReceiveTask).ConfigureAwait(false);
        await AwaitCancellationSilentlyAsync(_broadcastReceiveTask).ConfigureAwait(false);
        await AwaitCancellationSilentlyAsync(_keepAliveTask).ConfigureAwait(false);

        _cts?.Dispose();
        _cts = null;
        _relayReceiveTask = null;
        _broadcastReceiveTask = null;
        _keepAliveTask = null;

        IsRunning = false;
    }

    private void SendRegistrationPing(ParsedTunnelConfig config)
    {
        var ping = BuildRegistrationPing(config.NetworkIdBytes, config.OverlayIpBytes);

        try
        {
            _relayClient?.Send(ping, ping.Length);
        }
        catch (SocketException ex)
        {
            logger.LogDebug(ex, "Failed to send initial registration ping to relay.");
        }
    }

    private async Task KeepAliveLoopAsync(ParsedTunnelConfig config, CancellationToken ct)
    {
        var ping = BuildRegistrationPing(config.NetworkIdBytes, config.OverlayIpBytes);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(OnlineConstants.KeepAliveIntervalSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    if (_relayClient != null)
                    {
                        await _relayClient.SendAsync(ping, ping.Length).ConfigureAwait(false);
                    }
                }
                catch (SocketException ex)
                {
                    logger.LogDebug(ex, "Relay keepalive send failed: {Message}", ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Clean exit
        }
    }

    private async Task RelayReceiveLoopAsync(UdpClient client, ParsedTunnelConfig config, CancellationToken ct)
    {
        _localSender = new UdpClient();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await client.ReceiveAsync(ct).ConfigureAwait(false);
                var data = result.Buffer;

                if (!IsValidRelayPacket(data, config, out var isBroadcast, out var payloadLength))
                {
                    continue;
                }

                var payload = new byte[payloadLength];
                Buffer.BlockCopy(data, 24, payload, 0, payloadLength);

                try
                {
                    var targetEp = new IPEndPoint(IPAddress.Loopback, OnlineConstants.ZeroHourDiscoveryPort);
                    await _localSender.SendAsync(payload, payload.Length, targetEp).ConfigureAwait(false);
                }
                catch (SocketException ex)
                {
                    logger.LogDebug(ex, "Failed to deliver relayed packet locally: {Message}", ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Clean exit
        }
        catch (ObjectDisposedException)
        {
            // Clean exit on client disposal
        }
    }

    private async Task BroadcastReceiveLoopAsync(UdpClient listener, ParsedTunnelConfig config, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await listener.ReceiveAsync(ct).ConfigureAwait(false);
                var data = result.Buffer;

                if (!ShouldRelayBroadcast(result.RemoteEndPoint.Address, config.OverlayIp))
                {
                    continue;
                }

                var packet = new byte[24 + data.Length];
                Buffer.BlockCopy(config.NetworkIdBytes, 0, packet, 0, 16);
                Buffer.BlockCopy(config.DirectedBroadcastBytes, 0, packet, 16, 4);
                Buffer.BlockCopy(config.OverlayIpBytes, 0, packet, 20, 4);
                Buffer.BlockCopy(data, 0, packet, 24, data.Length);

                try
                {
                    if (_relayClient != null)
                    {
                        await _relayClient.SendAsync(packet, packet.Length).ConfigureAwait(false);
                    }
                }
                catch (SocketException ex)
                {
                    logger.LogDebug(ex, "Failed to relay broadcast packet: {Message}", ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Clean exit
        }
        catch (ObjectDisposedException)
        {
            // Clean exit on listener disposal
        }
    }

    private sealed record ParsedTunnelConfig(
        string NetworkId,
        byte[] NetworkIdBytes,
        IPAddress OverlayIp,
        byte[] OverlayIpBytes,
        IPEndPoint RelayEndpoint,
        int PrefixLength,
        byte[] DirectedBroadcastBytes);
}
