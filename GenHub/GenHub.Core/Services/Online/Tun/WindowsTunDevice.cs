using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Online;
using GenHub.Core.Models.Results;
using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Services.Online.Tun;

/// <summary>
/// Windows TUN virtual network adapter device implemented via Wintun.
/// </summary>
public sealed class WindowsTunDevice : ITunDevice
{
    private const uint RingCapacity = 0x400000; // 4 MB
    private static readonly Guid AdapterGuid = new("6a9c138b-d731-482a-a92c-55c4d0928e19");

    private readonly IntPtr _adapter;
    private readonly IntPtr _session;
    private readonly SafeWaitHandle _readWaitEvent;
    private bool _disposed;

    private WindowsTunDevice(
        string interfaceName,
        IPAddress overlayIp,
        IntPtr adapter,
        IntPtr session,
        IntPtr readWaitEvent)
    {
        InterfaceName = interfaceName;
        OverlayIp = overlayIp;
        _adapter = adapter;
        _session = session;
        _readWaitEvent = new SafeWaitHandle(readWaitEvent, ownsHandle: false);
    }

    /// <inheritdoc/>
    public string InterfaceName { get; }

    /// <inheritdoc/>
    public IPAddress OverlayIp { get; }

    /// <summary>
    /// Creates or opens a Wintun network adapter, configures its IPv4 address, and starts a session.
    /// </summary>
    /// <param name="interfaceName">The adapter name (e.g., "GenHub").</param>
    /// <param name="overlayIp">The overlay IPv4 address.</param>
    /// <param name="prefixLength">The subnet prefix length (e.g., 20).</param>
    /// <param name="mtu">The MTU to configure.</param>
    /// <returns>The created TUN device or an error.</returns>
    public static OperationResult<WindowsTunDevice> CreateOrOpen(
        string interfaceName,
        IPAddress overlayIp,
        int prefixLength = 20,
        int mtu = 1400)
    {
        if (!OperatingSystem.IsWindows())
        {
            return OperationResult<WindowsTunDevice>.CreateFailure("Wintun is only supported on Windows.");
        }

        if (!WintunNative.EnsureLoaded())
        {
            return OperationResult<WindowsTunDevice>.CreateFailure("Failed to load wintun.dll. Verify system architecture.");
        }

        IntPtr adapter = IntPtr.Zero;
        try
        {
            adapter = WintunNative.OpenAdapter!(interfaceName);
            if (adapter == IntPtr.Zero)
            {
                var guid = AdapterGuid;
                adapter = WintunNative.CreateAdapter!(interfaceName, "GenHub", in guid);
            }
        }
        catch (Win32Exception ex)
        {
            return OperationResult<WindowsTunDevice>.CreateFailure($"Wintun adapter creation failed: {ex.Message} (code {ex.NativeErrorCode}).");
        }

        if (adapter == IntPtr.Zero)
        {
            var errorCode = Marshal.GetLastWin32Error();
            var message = errorCode == 5
                ? "Administrator privileges are required to create the network adapter on Windows."
                : $"Failed to create or open Wintun adapter '{interfaceName}' (Win32 error {errorCode}).";
            return OperationResult<WindowsTunDevice>.CreateFailure(message);
        }

        // Configure IP address and MTU
        var mask = PrefixLengthToSubnetMask(prefixLength);
        ConfigureInterfaceViaNetsh(interfaceName, overlayIp.ToString(), mask, mtu);

        // Start TUN session
        var session = WintunNative.StartSession!(adapter, RingCapacity);
        if (session == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            WintunNative.CloseAdapter!(adapter);
            return OperationResult<WindowsTunDevice>.CreateFailure($"Failed to start Wintun session (Win32 error {err}).");
        }

        var readEvent = WintunNative.GetReadWaitEvent!(session);
        return OperationResult<WindowsTunDevice>.CreateSuccess(
            new WindowsTunDevice(interfaceName, overlayIp, adapter, session, readEvent));
    }

    /// <inheritdoc/>
    public async Task<int> ReadPacketAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        while (!cancellationToken.IsCancellationRequested)
        {
            var packetPtr = WintunNative.ReceivePacket!(_session, out var packetSize);
            if (packetPtr != IntPtr.Zero)
            {
                var copySize = Math.Min((int)packetSize, buffer.Length);
                Marshal.Copy(packetPtr, buffer, 0, copySize);
                WintunNative.ReleaseReceivePacket!(_session, packetPtr);
                return copySize;
            }

            // No packet ready; wait on the read-wait event asynchronously
            await WaitForEventAsync(_readWaitEvent, cancellationToken).ConfigureAwait(false);
        }

        return 0;
    }

    /// <inheritdoc/>
    public ValueTask WritePacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (packet.Length == 0 || cancellationToken.IsCancellationRequested)
        {
            return ValueTask.CompletedTask;
        }

        var sendPtr = WintunNative.AllocateSendPacket!(_session, (uint)packet.Length);
        if (sendPtr == IntPtr.Zero)
        {
            // Ring buffer full; packet dropped
            return ValueTask.CompletedTask;
        }

        Marshal.Copy(packet.ToArray(), 0, sendPtr, packet.Length);
        WintunNative.SendPacket!(_session, sendPtr);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_session != IntPtr.Zero)
        {
            try
            {
                WintunNative.EndSession?.Invoke(_session);
            }
            catch
            {
                // Best effort session cleanup
            }
        }

        if (_adapter != IntPtr.Zero)
        {
            try
            {
                WintunNative.CloseAdapter?.Invoke(_adapter);
            }
            catch
            {
                // Best effort adapter cleanup
            }
        }
    }

    private static string PrefixLengthToSubnetMask(int prefixLength)
    {
        if (prefixLength is < 0 or > 32)
        {
            return OnlineConstants.DefaultTunSubnetMask;
        }

        uint mask = prefixLength == 0 ? 0 : 0xFFFFFFFF << (32 - prefixLength);
        var bytes = BitConverter.GetBytes(mask);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.{bytes[3]}";
    }

    [SuppressMessage("Security", "S4036:ProcessStartInfo.FileName should not be relative", Justification = "netsh.exe path is resolved via SpecialFolder.System")]
    private static void ConfigureInterfaceViaNetsh(string interfaceName, string ipAddress, string mask, int mtu)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ResolveNetshPath(),
                    Arguments = $"interface ipv4 set address name=\"{interfaceName}\" source=static addr={ipAddress} mask={mask}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            proc.Start();
            proc.WaitForExit(3000);
        }
        catch
        {
            // Fallback: Continue even if netsh invocation fails
        }

        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ResolveNetshPath(),
                    Arguments = $"interface ipv4 set subinterface name=\"{interfaceName}\" mtu={mtu} store=active",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            proc.Start();
            proc.WaitForExit(2000);
        }
        catch
        {
            // Best effort MTU configuration
        }
    }

    private static string ResolveNetshPath()
    {
        var systemFolder = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var netshPath = string.IsNullOrEmpty(systemFolder) ? "netsh.exe" : Path.Combine(systemFolder, "netsh.exe");
        return File.Exists(netshPath) ? netshPath : "netsh.exe";
    }

    private static async Task WaitForEventAsync(SafeWaitHandle handle, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        var waitHandle = new ManualResetEvent(false) { SafeWaitHandle = handle };
        var rwh = ThreadPool.RegisterWaitForSingleObject(
            waitHandle,
            (_, _) => tcs.TrySetResult(true),
            null,
            -1,
            executeOnlyOnce: true);

        try
        {
            await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            rwh.Unregister(null);
        }
    }
}
