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
        private readonly TaskCompletionSource<bool> _packetWrittenTcs = new();

        public string InterfaceName => "fake0";

        public IPAddress OverlayIp => IPAddress.Parse("10.42.0.2");

        public IReadOnlyList<byte[]> WrittenPackets => _writtenPackets;

        public Task<int> ReadPacketAsync(byte[] buffer, CancellationToken cancellationToken)
        {
            // Block until canceled to simulate idle TUN
            var tcs = new TaskCompletionSource<int>();
            cancellationToken.Register(() => tcs.TrySetCanceled());
            return tcs.Task;
        }

        public async Task<bool> WaitForPacketWrittenAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            cts.Token.Register(() => _packetWrittenTcs.TrySetResult(false));
            return await _packetWrittenTcs.Task;
        }

        public ValueTask WritePacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
        {
            _writtenPackets.Add(packet.ToArray());
            _packetWrittenTcs.TrySetResult(true);
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
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

        // Send from relay back to client endpoint
        await relayServer.SendAsync(frame, frame.Length, receivedPing.RemoteEndPoint);

        // Assert packet is delivered to TUN
        var written = await fakeTun.WaitForPacketWrittenAsync(TimeSpan.FromSeconds(3));
        Assert.True(written, "Packet was not written to TUN device");
        Assert.Single(fakeTun.WrittenPackets);
        Assert.Equal(dummyIpPacket, fakeTun.WrittenPackets[0]);

        // Cleanup
        await pump.StopAsync();
    }
}
