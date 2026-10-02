using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Online;
using GenHub.Core.Models.Results;
using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Services.Online.Tun;

/// <summary>
/// An attached Linux TUN device streaming raw IP packets.
/// </summary>
public sealed class LinuxTunDevice : ITunDevice
{
    private readonly SafeFileHandle _handle;
    private readonly FileStream _stream;
    private bool _disposed;

    private LinuxTunDevice(string interfaceName, int fd, IPAddress? overlayIp = null)
    {
        InterfaceName = interfaceName;
        OverlayIp = overlayIp ?? IPAddress.Any;
        _handle = new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
        _stream = new FileStream(_handle, FileAccess.ReadWrite);
    }

    /// <summary>
    /// Gets the interface name.
    /// </summary>
    public string InterfaceName { get; }

    /// <inheritdoc/>
    public IPAddress OverlayIp { get; }

    /// <summary>
    /// Gets the packet stream. Reads and writes complete IP packets (no packet info header).
    /// </summary>
    public Stream Stream => _stream;

    /// <summary>
    /// Attaches to an existing persistent TUN interface.
    /// </summary>
    /// <param name="interfaceName">The interface name.</param>
    /// <param name="overlayIp">Optional overlay IP address assigned to this interface.</param>
    /// <returns>The attached device.</returns>
    public static OperationResult<LinuxTunDevice> Attach(string interfaceName, IPAddress? overlayIp = null)
    {
        if (!OperatingSystem.IsLinux())
        {
            return OperationResult<LinuxTunDevice>.CreateFailure("TUN interfaces are only supported on Linux.");
        }

        if (!LinuxTunNative.IsValidInterfaceName(interfaceName))
        {
            return OperationResult<LinuxTunDevice>.CreateFailure($"Invalid TUN interface name '{interfaceName}'.");
        }

        if (!LinuxTunInterface.Exists(interfaceName))
        {
            return OperationResult<LinuxTunDevice>.CreateFailure($"TUN interface '{interfaceName}' was not found (run privileged setup first).");
        }

        var (fd, openErrno) = LinuxTunNative.OpenControlDevice();
        if (fd < 0)
        {
            return OperationResult<LinuxTunDevice>.CreateFailure($"Cannot open {OnlineConstants.TunDevicePath} ({LinuxTunNative.DescribeErrno(openErrno)}).");
        }

        var attached = LinuxTunNative.AttachInterface(fd, interfaceName);
        if (attached != 0)
        {
            LinuxTunNative.CloseDevice(fd);
            return OperationResult<LinuxTunDevice>.CreateFailure($"Cannot attach TUN interface '{interfaceName}' ({LinuxTunNative.DescribeErrno(attached)}).");
        }

        return OperationResult<LinuxTunDevice>.CreateSuccess(new LinuxTunDevice(interfaceName, fd, overlayIp));
    }

    /// <inheritdoc/>
    public Task<int> ReadPacketAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask WritePacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _stream.WriteAsync(packet, cancellationToken);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stream.Dispose();
        _handle.Dispose();
    }
}
