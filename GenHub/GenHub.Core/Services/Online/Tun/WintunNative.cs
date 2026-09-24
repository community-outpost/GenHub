using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace GenHub.Core.Services.Online.Tun;

/// <summary>
/// Dynamic P/Invoke delegate mappings for wintun.dll virtual network adapter driver.
/// </summary>
internal static class WintunNative
{
    private static readonly object InitLock = new();
    private static IntPtr _moduleHandle = IntPtr.Zero;
    private static bool _initialized;

    /// <summary>Creates a new Wintun adapter.</summary>
    /// <param name="name">The adapter name.</param>
    /// <param name="tunnelType">The adapter type string.</param>
    /// <param name="requestedGuid">Optional GUID.</param>
    /// <returns>Adapter pointer or IntPtr.Zero on failure.</returns>
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    internal delegate IntPtr WintunCreateAdapterFunc(
        [MarshalAs(UnmanagedType.LPWStr)] string name,
        [MarshalAs(UnmanagedType.LPWStr)] string tunnelType,
        in Guid requestedGuid);

    /// <summary>Opens an existing Wintun adapter by name.</summary>
    /// <param name="name">The adapter name.</param>
    /// <returns>Adapter pointer or IntPtr.Zero if not found.</returns>
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    internal delegate IntPtr WintunOpenAdapterFunc(
        [MarshalAs(UnmanagedType.LPWStr)] string name);

    /// <summary>Closes a Wintun adapter handle.</summary>
    /// <param name="adapter">The adapter pointer.</param>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void WintunCloseAdapterFunc(IntPtr adapter);

    /// <summary>Gets the LUID of an adapter.</summary>
    /// <param name="adapter">The adapter pointer.</param>
    /// <param name="luid">The output LUID.</param>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void WintunGetAdapterLUIDFunc(IntPtr adapter, out ulong luid);

    /// <summary>Starts a Wintun adapter session.</summary>
    /// <param name="adapter">The adapter pointer.</param>
    /// <param name="capacity">Ring capacity in bytes.</param>
    /// <returns>Session pointer or IntPtr.Zero on failure.</returns>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate IntPtr WintunStartSessionFunc(IntPtr adapter, uint capacity);

    /// <summary>Ends a Wintun adapter session.</summary>
    /// <param name="session">The session pointer.</param>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void WintunEndSessionFunc(IntPtr session);

    /// <summary>Gets the read wait event handle for a session.</summary>
    /// <param name="session">The session pointer.</param>
    /// <returns>Event handle.</returns>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate IntPtr WintunGetReadWaitEventFunc(IntPtr session);

    /// <summary>Receives a packet from the session ring buffer.</summary>
    /// <param name="session">The session pointer.</param>
    /// <param name="packetSize">The received packet size.</param>
    /// <returns>Pointer to packet bytes or IntPtr.Zero if none ready.</returns>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate IntPtr WintunReceivePacketFunc(IntPtr session, out uint packetSize);

    /// <summary>Releases a previously received packet pointer.</summary>
    /// <param name="session">The session pointer.</param>
    /// <param name="packet">The packet pointer.</param>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void WintunReleaseReceivePacketFunc(IntPtr session, IntPtr packet);

    /// <summary>Allocates a send packet in the session ring buffer.</summary>
    /// <param name="session">The session pointer.</param>
    /// <param name="packetSize">Size to allocate.</param>
    /// <returns>Pointer to buffer or IntPtr.Zero if ring is full.</returns>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate IntPtr WintunAllocateSendPacketFunc(IntPtr session, uint packetSize);

    /// <summary>Sends an allocated packet into the virtual adapter.</summary>
    /// <param name="session">The session pointer.</param>
    /// <param name="packet">The packet pointer.</param>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void WintunSendPacketFunc(IntPtr session, IntPtr packet);

    /// <summary>Gets the WintunCreateAdapter delegate.</summary>
    internal static WintunCreateAdapterFunc? CreateAdapter { get; private set; }

    /// <summary>Gets the WintunOpenAdapter delegate.</summary>
    internal static WintunOpenAdapterFunc? OpenAdapter { get; private set; }

    /// <summary>Gets the WintunCloseAdapter delegate.</summary>
    internal static WintunCloseAdapterFunc? CloseAdapter { get; private set; }

    /// <summary>Gets the WintunGetAdapterLUID delegate.</summary>
    internal static WintunGetAdapterLUIDFunc? GetAdapterLUID { get; private set; }

    /// <summary>Gets the WintunStartSession delegate.</summary>
    internal static WintunStartSessionFunc? StartSession { get; private set; }

    /// <summary>Gets the WintunEndSession delegate.</summary>
    internal static WintunEndSessionFunc? EndSession { get; private set; }

    /// <summary>Gets the WintunGetReadWaitEvent delegate.</summary>
    internal static WintunGetReadWaitEventFunc? GetReadWaitEvent { get; private set; }

    /// <summary>Gets the WintunReceivePacket delegate.</summary>
    internal static WintunReceivePacketFunc? ReceivePacket { get; private set; }

    /// <summary>Gets the WintunReleaseReceivePacket delegate.</summary>
    internal static WintunReleaseReceivePacketFunc? ReleaseReceivePacket { get; private set; }

    /// <summary>Gets the WintunAllocateSendPacket delegate.</summary>
    internal static WintunAllocateSendPacketFunc? AllocateSendPacket { get; private set; }

    /// <summary>Gets the WintunSendPacket delegate.</summary>
    internal static WintunSendPacketFunc? SendPacket { get; private set; }

    /// <summary>
    /// Ensures wintun.dll is unpacked from embedded resources (matching host architecture) and loaded.
    /// </summary>
    /// <returns>True if loaded successfully, false otherwise.</returns>
    internal static bool EnsureLoaded()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        lock (InitLock)
        {
            if (_initialized)
            {
                return _moduleHandle != IntPtr.Zero;
            }

            _initialized = true;

            var archFolder = GetArchitectureFolder();
            if (archFolder == null)
            {
                return false;
            }

            var targetDll = ExtractEmbeddedWintunDll(archFolder);
            _moduleHandle = TryLoadWintunModule(targetDll);
            if (_moduleHandle == IntPtr.Zero)
            {
                return false;
            }

            ResolveDelegates(_moduleHandle);
            return true;
        }
    }

    private static string? GetArchitectureFolder() =>
        RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            _ => null,
        };

    private static string ExtractEmbeddedWintunDll(string archFolder)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var targetDir = Path.Combine(localAppData, "GenHub", "overlay", archFolder);
        Directory.CreateDirectory(targetDir);
        var targetDll = Path.Combine(targetDir, "wintun.dll");

        var resourceName = $"GenHub.Core.Resources.Wintun.{archFolder}.wintun.dll";
        using var resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (resourceStream != null && ShouldExtractResource(targetDll, resourceStream.Length))
        {
            try
            {
                using var fileStream = File.Create(targetDll);
                resourceStream.CopyTo(fileStream);
            }
            catch
            {
                // Best effort write
            }
        }

        return targetDll;
    }

    private static bool ShouldExtractResource(string targetDll, long expectedLength)
    {
        if (!File.Exists(targetDll))
        {
            return true;
        }

        try
        {
            var existing = new FileInfo(targetDll);
            return existing.Length != expectedLength;
        }
        catch
        {
            return true;
        }
    }

    private static IntPtr TryLoadWintunModule(string targetDll)
    {
        if (File.Exists(targetDll) && NativeLibrary.TryLoad(targetDll, out var handle))
        {
            return handle;
        }

        if (NativeLibrary.TryLoad("wintun.dll", out handle))
        {
            return handle;
        }

        return IntPtr.Zero;
    }

    private static void ResolveDelegates(IntPtr moduleHandle)
    {
        CreateAdapter = Marshal.GetDelegateForFunctionPointer<WintunCreateAdapterFunc>(
            NativeLibrary.GetExport(moduleHandle, "WintunCreateAdapter"));
        OpenAdapter = Marshal.GetDelegateForFunctionPointer<WintunOpenAdapterFunc>(
            NativeLibrary.GetExport(moduleHandle, "WintunOpenAdapter"));
        CloseAdapter = Marshal.GetDelegateForFunctionPointer<WintunCloseAdapterFunc>(
            NativeLibrary.GetExport(moduleHandle, "WintunCloseAdapter"));
        GetAdapterLUID = Marshal.GetDelegateForFunctionPointer<WintunGetAdapterLUIDFunc>(
            NativeLibrary.GetExport(moduleHandle, "WintunGetAdapterLUID"));
        StartSession = Marshal.GetDelegateForFunctionPointer<WintunStartSessionFunc>(
            NativeLibrary.GetExport(moduleHandle, "WintunStartSession"));
        EndSession = Marshal.GetDelegateForFunctionPointer<WintunEndSessionFunc>(
            NativeLibrary.GetExport(moduleHandle, "WintunEndSession"));
        GetReadWaitEvent = Marshal.GetDelegateForFunctionPointer<WintunGetReadWaitEventFunc>(
            NativeLibrary.GetExport(moduleHandle, "WintunGetReadWaitEvent"));
        ReceivePacket = Marshal.GetDelegateForFunctionPointer<WintunReceivePacketFunc>(
            NativeLibrary.GetExport(moduleHandle, "WintunReceivePacket"));
        ReleaseReceivePacket = Marshal.GetDelegateForFunctionPointer<WintunReleaseReceivePacketFunc>(
            NativeLibrary.GetExport(moduleHandle, "WintunReleaseReceivePacket"));
        AllocateSendPacket = Marshal.GetDelegateForFunctionPointer<WintunAllocateSendPacketFunc>(
            NativeLibrary.GetExport(moduleHandle, "WintunAllocateSendPacket"));
        SendPacket = Marshal.GetDelegateForFunctionPointer<WintunSendPacketFunc>(
            NativeLibrary.GetExport(moduleHandle, "WintunSendPacket"));
    }
}
