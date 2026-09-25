using GenHub.Core.Interfaces.Online;
using GenHub.Core.Services.Online.Tun;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Online;

/// <summary>
/// Unit tests for <see cref="TunPacketPump"/>.
/// </summary>
public class TunPacketPumpTests
{
    private sealed class FakeTunDevice : ITunDevice
    {
        private readonly List<byte[]> _writtenPackets = new();
        private readonly SemaphoreSlim _packetSignal = new(0);

        public string InterfaceName => "fake0";

        public IPAddress OverlayIp => IPAddress.Parse("10.42.0.2");

        public IReadOnlyList<byte[]> WrittenPackets
        {
            get
            {
                lock (_writtenPackets)
                {
                    return _writtenPackets.ToList();
                }
            }
        }

        public Task<int> ReadPacketAsync(byte[] buffer, CancellationToken cancellationToken)
        {
            // Block until canceled to simulate idle TUN
            var tcs = new TaskCompletionSource<int>();
            cancellationToken.Register(() => tcs.TrySetCanceled());
            return tcs.Task;
        }

        public async Task<bool> WaitForPacketWrittenAsync(TimeSpan timeout)
        {
            return await _packetSignal.WaitAsync(timeout);
        }

        public ValueTask WritePacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
        {
            lock (_writtenPackets)
            {
                _writtenPackets.Add(packet.ToArray());
            }

            _packetSignal.Release();
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            _packetSignal.Dispose();
        }
    }

    /// <summary>
    /// Verifies that packets received from the relay matching our overlay IP are written to the TUN device.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task PumpAsync_ReceivesRelayPacket_WritesToTunDevice()
    {
        // Arrange
        using var relayServer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var relayPort = ((IPEndPoint)relayServer.Client.LocalEndPoint!).Port;
        var relayEndpoint = new IPEndPoint(IPAddress.Loopback, relayPort);

        var fakeTun = new FakeTunDevice();
        using var pump = new TunPacketPump(
            fakeTun,
            relayEndpoint,
            "00112233445566778899aabbccddeeff",
            IPAddress.Parse("10.42.0.2"),
            NullLogger.Instance);

        pump.Start();

        // Wait for pump's keepalive ping to reach relay server
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var pingReceiveTask = relayServer.ReceiveAsync(cts.Token);
        var receivedPing = await pingReceiveTask;
        Assert.Equal(24, receivedPing.Buffer.Length);

        // Build a framed payload from relay to TUN device
        // Minimum valid IPv4 packet is 20 bytes header + optional payload
        var dummyIpPacket = new byte[24];
        dummyIpPacket[0] = 0x45; // IPv4, Version 4, IHL 5 (20 bytes header)
        dummyIpPacket[20] = 0xDE;
        dummyIpPacket[21] = 0xAD;
        dummyIpPacket[22] = 0xBE;
        dummyIpPacket[23] = 0xEF;

        // 16 bytes NetworkId + 4 bytes TargetIp (10.42.0.2) + 4 bytes SourceIp (10.42.0.3) + payload
        var frame = new byte[24 + dummyIpPacket.Length];
        var networkIdBytes = Convert.FromHexString("00112233445566778899aabbccddeeff");
        networkIdBytes.CopyTo(frame, 0);
        IPAddress.Parse("10.42.0.2").GetAddressBytes().CopyTo(frame, 16);
        IPAddress.Parse("10.42.0.3").GetAddressBytes().CopyTo(frame, 20);
        dummyIpPacket.CopyTo(frame, 24);

        // Send from relay back to client endpoint with retry for UDP reliability under test runner load
        using var retryCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!retryCts.Token.IsCancellationRequested && fakeTun.WrittenPackets.Count == 0)
        {
            await relayServer.SendAsync(frame, frame.Length, receivedPing.RemoteEndPoint);
            if (await fakeTun.WaitForPacketWrittenAsync(TimeSpan.FromMilliseconds(250)))
            {
                break;
            }
        }

        // Assert packet is delivered to TUN
        Assert.NotEmpty(fakeTun.WrittenPackets);
        Assert.Equal(dummyIpPacket, fakeTun.WrittenPackets[0]);

        // Cleanup
        await pump.StopAsync();
    }

    /// <summary>
    /// Verifies that directed broadcast destination IP is rewritten to 255.255.255.255.
    /// </summary>
    [Fact]
    public void TryBuildOutboundFrame_DirectedBroadcast_RewritesTargetTo255()
    {
        var fakeTun = new FakeTunDevice();
        using var pump = new TunPacketPump(
            fakeTun,
            new IPEndPoint(IPAddress.Loopback, 8088),
            "00112233445566778899aabbccddeeff",
            IPAddress.Parse("10.42.0.2"),
            NullLogger.Instance,
            prefixLength: 20);

        var packet = new byte[20];
        packet[0] = 0x45;

        // Dest IP = 10.42.15.255 (directed broadcast for 10.42.0.0/20)
        packet[16] = 10;
        packet[17] = 42;
        packet[18] = 15;
        packet[19] = 255;

        var success = pump.TryBuildOutboundFrame(packet, packet.Length, IPAddress.Parse("10.42.0.2").GetAddressBytes(), out var frame);

        Assert.True(success);
        Assert.NotNull(frame);

        // Header target bytes (16..19) should be rewritten to 255.255.255.255
        Assert.Equal(255, frame[16]);
        Assert.Equal(255, frame[17]);
        Assert.Equal(255, frame[18]);
        Assert.Equal(255, frame[19]);
    }

    /// <summary>
    /// Verifies that unicast destination IP is preserved in outbound frame header.
    /// </summary>
    [Fact]
    public void TryBuildOutboundFrame_Unicast_PreservesTarget()
    {
        var fakeTun = new FakeTunDevice();
        using var pump = new TunPacketPump(
            fakeTun,
            new IPEndPoint(IPAddress.Loopback, 8088),
            "00112233445566778899aabbccddeeff",
            IPAddress.Parse("10.42.0.2"),
            NullLogger.Instance);

        var packet = new byte[20];
        packet[0] = 0x45;
        packet[16] = 10;
        packet[17] = 42;
        packet[18] = 0;
        packet[19] = 5;

        var success = pump.TryBuildOutboundFrame(packet, packet.Length, IPAddress.Parse("10.42.0.2").GetAddressBytes(), out var frame);

        Assert.True(success);
        Assert.NotNull(frame);
        Assert.Equal(10, frame[16]);
        Assert.Equal(42, frame[17]);
        Assert.Equal(0, frame[18]);
        Assert.Equal(5, frame[19]);
    }

    /// <summary>
    /// Verifies that inbound broadcast frames originated by this node are dropped.
    /// </summary>
    [Fact]
    public void IsInboundFrameForUs_SelfEchoBroadcast_DropsPacket()
    {
        var fakeTun = new FakeTunDevice();
        using var pump = new TunPacketPump(
            fakeTun,
            new IPEndPoint(IPAddress.Loopback, 8088),
            "00112233445566778899aabbccddeeff",
            IPAddress.Parse("10.42.0.2"),
            NullLogger.Instance);

        var frame = new byte[44];
        var netId = Convert.FromHexString("00112233445566778899aabbccddeeff");
        netId.CopyTo(frame, 0);

        // Target: 255.255.255.255
        frame[16] = 255;
        frame[17] = 255;
        frame[18] = 255;
        frame[19] = 255;

        // Source: 10.42.0.2 (self)
        frame[20] = 10;
        frame[21] = 42;
        frame[22] = 0;
        frame[23] = 2;
        frame[24] = 0x45;

        var accepted = pump.IsInboundFrameForUs(frame, IPAddress.Parse("10.42.0.2").GetAddressBytes());
        Assert.False(accepted);
    }

    /// <summary>
    /// Verifies that inbound frames matching a different NetworkId are rejected.
    /// </summary>
    [Fact]
    public void IsInboundFrameForUs_WrongNetworkId_DropsPacket()
    {
        var fakeTun = new FakeTunDevice();
        using var pump = new TunPacketPump(
            fakeTun,
            new IPEndPoint(IPAddress.Loopback, 8088),
            "00112233445566778899aabbccddeeff",
            IPAddress.Parse("10.42.0.2"),
            NullLogger.Instance);

        var frame = new byte[44];
        var wrongNetId = Convert.FromHexString("ffffffffffffffffffffffffffffffff");
        wrongNetId.CopyTo(frame, 0);

        // Target: 10.42.0.2
        frame[16] = 10;
        frame[17] = 42;
        frame[18] = 0;
        frame[19] = 2;
        frame[20] = 10;
        frame[21] = 42;
        frame[22] = 0;
        frame[23] = 3;
        frame[24] = 0x45;

        var accepted = pump.IsInboundFrameForUs(frame, IPAddress.Parse("10.42.0.2").GetAddressBytes());
        Assert.False(accepted);
    }
}
