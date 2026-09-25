using GenHub.Core.Constants;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

namespace GenHub.Core.Services.Online.Tun;

/// <summary>
/// Native libc interop for Linux TUN devices.
/// </summary>
internal static class LinuxTunNative
{
    private const int MaxInterfaceNameLength = 15;
    private const int InterfaceRequestSize = 40;
    private const int InterfaceFlagsOffset = 16;
    private const short InterfaceFlagsTunNoPacketInfo = 0x1001;
    private const ulong IoctlSetInterface = 0x400454CA;
    private const ulong IoctlSetPersistent = 0x400454CB;
    private const ulong IoctlSetOwner = 0x400454CC;
    private const int OpenReadWrite = 0x80002; // O_RDWR | O_CLOEXEC
    private const int ErrnoPermission = 1;
    private const int ErrnoAccess = 13;

    /// <summary>
    /// Opens the TUN control device.
    /// </summary>
    /// <returns>The file descriptor and errno (zero on success).</returns>
    internal static (int Fd, int Errno) OpenControlDevice()
    {
        var fd = Open(OnlineConstants.TunDevicePath, OpenReadWrite);
        if (fd < 0)
        {
            return (fd, Marshal.GetLastWin32Error());
        }

        return (fd, 0);
    }

    /// <summary>
    /// Closes a control device descriptor if valid.
    /// </summary>
    /// <param name="fd">The file descriptor.</param>
    /// <returns>True if closed successfully; otherwise, false.</returns>
    internal static bool CloseDevice(int fd)
    {
        if (fd < 0)
        {
            return false;
        }

        return Close(fd) == 0;
    }

    /// <summary>
    /// Attaches a control device descriptor to a named interface, creating it when absent.
    /// </summary>
    /// <param name="fd">The control device descriptor.</param>
    /// <param name="interfaceName">The validated interface name.</param>
    /// <returns>Zero on success, otherwise the errno.</returns>
    internal static int AttachInterface(int fd, string interfaceName)
    {
        var request = BuildInterfaceRequest(interfaceName);
        if (Ioctl(fd, IoctlSetInterface, request) < 0)
        {
            return Marshal.GetLastWin32Error();
        }

        return 0;
    }

    /// <summary>
    /// Sets interface persistence.
    /// </summary>
    /// <param name="fd">The attached descriptor.</param>
    /// <param name="persistent">Whether the interface survives descriptor close.</param>
    /// <returns>Zero on success, otherwise the errno.</returns>
    internal static int SetPersistent(int fd, bool persistent)
    {
        if (Ioctl(fd, IoctlSetPersistent, persistent ? 1 : 0) < 0)
        {
            return Marshal.GetLastWin32Error();
        }

        return 0;
    }

    /// <summary>
    /// Assigns interface ownership to the calling user.
    /// </summary>
    /// <param name="fd">The attached descriptor.</param>
    /// <returns>Zero on success, otherwise the errno.</returns>
    internal static int SetOwner(int fd)
    {
        if (Ioctl(fd, IoctlSetOwner, unchecked((int)GetUid())) < 0)
        {
            return Marshal.GetLastWin32Error();
        }

        return 0;
    }

    /// <summary>
    /// Validates a TUN interface name against kernel limits.
    /// </summary>
    /// <param name="name">The candidate name.</param>
    /// <returns>True when the name is usable.</returns>
    internal static bool IsValidInterfaceName([NotNullWhen(true)] string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxInterfaceNameLength || name is "." or "..")
        {
            return false;
        }

        foreach (var character in name)
        {
            var valid = character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '-' or '.';
            if (!valid)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reports whether the process already runs with root privileges, in which
    /// case TUN provisioning needs no polkit/sudo escalation.
    /// </summary>
    /// <returns>True when the effective user id is root.</returns>
    internal static bool IsElevated()
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        return GetEffectiveUid() == 0;
    }

    /// <summary>
    /// Describes a native error number.
    /// </summary>
    /// <param name="errno">The error number.</param>
    /// <returns>The description.</returns>
    internal static string DescribeErrno(int errno)
    {
        if (errno is ErrnoPermission or ErrnoAccess)
        {
            return "permission denied (privileged setup required)";
        }

        return $"errno {errno}";
    }

    private static byte[] BuildInterfaceRequest(string interfaceName)
    {
        var request = new byte[InterfaceRequestSize];
        var nameBytes = Encoding.ASCII.GetBytes(interfaceName);
        Buffer.BlockCopy(nameBytes, 0, request, 0, nameBytes.Length);
        Buffer.BlockCopy(BitConverter.GetBytes(InterfaceFlagsTunNoPacketInfo), 0, request, InterfaceFlagsOffset, sizeof(short));
        return request;
    }

    [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, EntryPoint = "open")]
    private static extern int Open(string pathname, int flags);

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true, ExactSpelling = true)]
    private static extern int Ioctl(int fd, ulong request, [In] byte[] data);

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true, ExactSpelling = true)]
    private static extern int Ioctl(int fd, ulong request, int value);

    [DllImport("libc", ExactSpelling = true, EntryPoint = "getuid")]
    private static extern uint GetUid();

    [DllImport("libc", ExactSpelling = true, EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUid();

    [DllImport("libc", SetLastError = true, ExactSpelling = true, EntryPoint = "close")]
    private static extern int Close(int fd);
}
