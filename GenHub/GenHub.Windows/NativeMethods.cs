using System;
using System.Runtime.InteropServices;

namespace GenHub.Windows;

/// <summary>
/// Contains Win32 API constants and methods for interop.
/// </summary>
internal static class NativeMethods
{
    /// <summary>
    /// The command to restore a window.
    /// </summary>
    internal const int SW_RESTORE = 9;

    /// <summary>
    /// Brings the specified window to the foreground and activates it.
    /// </summary>
    /// <remarks>This method is a wrapper for the native Windows API function in user32.dll. The ability to
    /// bring a window to the foreground may be restricted by the system's user interface privilege level.</remarks>
    /// <param name="hWnd">A handle to the window that should be brought to the foreground.</param>
    /// <returns><see langword="true"/> if the window was successfully brought to the foreground; otherwise, <see
    /// langword="false"/>.</returns>
    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// Sets the specified window's show state.
    /// </summary>
    /// <remarks>
    /// This method is a wrapper for the native Windows API function in user32.dll. It can be used to minimize, maximize, restore, or hide a window.
    /// </remarks>
    /// <param name="hWnd">A handle to the window.</param>
    /// <param name="nCmdShow">Controls how the window is to be shown. For example, use <see cref="SW_RESTORE"/> to restore a minimized or maximized window.</param>
    /// <returns><see langword="true"/> if the window was previously visible; otherwise, <see langword="false"/>.</returns>
    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}