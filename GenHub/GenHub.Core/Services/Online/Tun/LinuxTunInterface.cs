using GenHub.Core.Constants;
using GenHub.Core.Models.Results;
using System;
using System.IO;

namespace GenHub.Core.Services.Online.Tun;

/// <summary>
/// Manages persistent Linux TUN interfaces for the overlay.
/// </summary>
/// <remarks>
/// Creating and deleting interfaces requires privileges; the sidecar itself only
/// attaches to an interface provisioned beforehand.
/// </remarks>
public static class LinuxTunInterface
{
    /// <summary>
    /// Checks whether a network interface exists.
    /// </summary>
    /// <param name="interfaceName">The interface name.</param>
    /// <returns>True when the interface exists.</returns>
    public static bool Exists(string interfaceName)
    {
        if (!LinuxTunNative.IsValidInterfaceName(interfaceName))
        {
            return false;
        }

        return Directory.Exists(Path.Combine(OnlineConstants.TunSysClassNetDirectory, interfaceName));
    }

    /// <summary>
    /// Creates a persistent TUN interface owned by the calling user.
    /// </summary>
    /// <param name="interfaceName">The interface name.</param>
    /// <returns>True when the interface was created.</returns>
    public static OperationResult<bool> TryCreatePersistent(string interfaceName)
    {
        if (!OperatingSystem.IsLinux())
        {
            return OperationResult<bool>.CreateFailure("TUN interfaces are only supported on Linux.");
        }

        if (!LinuxTunNative.IsValidInterfaceName(interfaceName))
        {
            return OperationResult<bool>.CreateFailure($"Invalid TUN interface name '{interfaceName}'.");
        }

        if (Exists(interfaceName))
        {
            return OperationResult<bool>.CreateFailure($"TUN interface '{interfaceName}' already exists.");
        }

        return CreatePersistentCore(interfaceName);
    }

    /// <summary>
    /// Deletes a persistent TUN interface. Missing interfaces succeed for idempotent cleanup.
    /// </summary>
    /// <param name="interfaceName">The interface name.</param>
    /// <returns>True when the interface is gone.</returns>
    public static OperationResult<bool> Delete(string interfaceName)
    {
        if (!OperatingSystem.IsLinux())
        {
            return OperationResult<bool>.CreateFailure("TUN interfaces are only supported on Linux.");
        }

        if (!LinuxTunNative.IsValidInterfaceName(interfaceName))
        {
            return OperationResult<bool>.CreateFailure($"Invalid TUN interface name '{interfaceName}'.");
        }

        if (!Exists(interfaceName))
        {
            return OperationResult<bool>.CreateSuccess(true);
        }

        var (fd, openErrno) = LinuxTunNative.OpenControlDevice();
        if (fd < 0)
        {
            return OperationResult<bool>.CreateFailure($"Cannot open {OnlineConstants.TunDevicePath} ({LinuxTunNative.DescribeErrno(openErrno)}).");
        }

        try
        {
            var attached = LinuxTunNative.AttachInterface(fd, interfaceName);
            if (attached != 0)
            {
                return OperationResult<bool>.CreateFailure($"Cannot remove TUN interface '{interfaceName}' ({LinuxTunNative.DescribeErrno(attached)}).");
            }

            var unpersisted = LinuxTunNative.SetPersistent(fd, false);
            if (unpersisted != 0)
            {
                return OperationResult<bool>.CreateFailure($"Cannot remove TUN interface '{interfaceName}' ({LinuxTunNative.DescribeErrno(unpersisted)}).");
            }

            return OperationResult<bool>.CreateSuccess(true);
        }
        finally
        {
            LinuxTunNative.CloseDevice(fd);
        }
    }

    private static OperationResult<bool> CreatePersistentCore(string interfaceName)
    {
        var (fd, openErrno) = LinuxTunNative.OpenControlDevice();
        if (fd < 0)
        {
            return OperationResult<bool>.CreateFailure($"Cannot open {OnlineConstants.TunDevicePath} ({LinuxTunNative.DescribeErrno(openErrno)}).");
        }

        try
        {
            var attached = LinuxTunNative.AttachInterface(fd, interfaceName);
            if (attached != 0)
            {
                return OperationResult<bool>.CreateFailure($"Cannot create TUN interface '{interfaceName}' ({LinuxTunNative.DescribeErrno(attached)}).");
            }

            var persisted = LinuxTunNative.SetPersistent(fd, true);
            if (persisted != 0)
            {
                return OperationResult<bool>.CreateFailure($"Cannot persist TUN interface '{interfaceName}' ({LinuxTunNative.DescribeErrno(persisted)}).");
            }

            var owned = LinuxTunNative.SetOwner(fd);
            if (owned != 0)
            {
                _ = LinuxTunNative.SetPersistent(fd, false);
                return OperationResult<bool>.CreateFailure($"Cannot assign TUN interface '{interfaceName}' ({LinuxTunNative.DescribeErrno(owned)}).");
            }

            return OperationResult<bool>.CreateSuccess(true);
        }
        finally
        {
            LinuxTunNative.CloseDevice(fd);
        }
    }
}
