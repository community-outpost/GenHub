using GenHub.Core.Constants;
using GenHub.Core.Models.Results;
using Microsoft.Win32.SafeHandles;
using System;
using System.IO;

namespace GenHub.Core.Services.Online.Tun;

/// <summary>
/// An attached Linux TUN device streaming raw IP packets.
/// </summary>
public sealed class LinuxTunDevice : IDisposable
{
    private readonly SafeFileHandle _handle;
    private readonly FileStream _stream;
    private bool _disposed;

    private LinuxTunDevice(string interfaceName, int fd)
    {
        InterfaceName = interfaceName;
        _handle = new SafeFileHandle(new IntPtr(fd), ownsHandle: true);
        _stream = new FileStream(_handle, FileAccess.ReadWrite);
    }

    /// <summary>
    /// Gets the interface name.
    /// </summary>
    public string InterfaceName { get; }

    /// <summary>
    /// Gets the packet stream. Reads and writes complete IP packets (no packet info header).
    /// </summary>
    public Stream Stream => _stream;

    /// <summary>
    /// Attaches to an existing persistent TUN interface.
    /// </summary>
    /// <param name="interfaceName">The interface name.</param>
    /// <returns>The attached device.</returns>
    public static OperationResult<LinuxTunDevice> Attach(string interfaceName)
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

        return OperationResult<LinuxTunDevice>.CreateSuccess(new LinuxTunDevice(interfaceName, fd));
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
