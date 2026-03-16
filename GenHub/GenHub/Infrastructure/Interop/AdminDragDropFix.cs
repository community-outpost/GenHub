using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;
using Avalonia.Platform;

namespace GenHub.Infrastructure.Interop;

/// <summary>
/// Enables drag and drop for elevated (Administrator) processes by bypassing UIPI.
/// </summary>
[SuppressMessage("StyleCop.CSharp.NamingRules", "SA1310:FieldNamesMustNotContainUnderscore", Justification = "Win32 Constants")]
public static partial class AdminDragDropFix
{
    // Standard drag-and-drop messages
    private const uint WM_DROPFILES = 0x0233;
    private const uint WM_COPYDATA = 0x004A;
    private const uint WM_COPYGLOBALDATA = 0x0049;

    // Additional OLE drag-and-drop messages
    private const uint WM_GETOBJECT = 0x003D;
    private const uint WM_DRAWCLIPBOARD = 0x0308;
    private const uint WM_CHANGECBCHAIN = 0x030D;

    // OLE drag-and-drop specific messages (used by IDropTarget interface)
    private const uint WM_USER = 0x0400;
    private const uint WM_DDE_FIRST = 0x03E0;
    private const uint WM_DDE_LAST = 0x03E8;

    private const uint MSGFLT_ALLOW = 1;
    private const int GWLP_WNDPROC = -4;

    // Diagnostic flag - can be set via environment variable
    private static readonly bool DiagnosticsEnabled =
        Environment.GetEnvironmentVariable("GENHUB_DIAGNOSE_DRAGDROP") == "1";

    [StructLayout(LayoutKind.Sequential)]
    private struct CHANGEFILTERSTRUCT
    {
        public uint CbSize;
        public uint ExtStatus;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint msg, uint action, ref CHANGEFILTERSTRUCT changeInfo);

    [LibraryImport("shell32.dll")]
    private static partial void DragAcceptFiles(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fAccept);

    [LibraryImport("shell32.dll", EntryPoint = "DragQueryFileW", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial uint DragQueryFile(IntPtr hDrop, uint iFile, char* lpszFile, uint cch);

    [LibraryImport("shell32.dll")]
    private static partial void DragFinish(IntPtr hDrop);

    [LibraryImport("ole32.dll")]
    private static partial int RevokeDragDrop(IntPtr hwnd);

    [LibraryImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static partial IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static partial int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        if (IntPtr.Size == 8)
            return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
        else
            return new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private class DragDropHook
    {
        private static string GetMessageName(uint msg)
        {
            return msg switch
            {
                WM_DROPFILES => "WM_DROPFILES",
                WM_COPYDATA => "WM_COPYDATA",
                WM_COPYGLOBALDATA => "WM_COPYGLOBALDATA",
                WM_GETOBJECT => "WM_GETOBJECT",
                WM_DRAWCLIPBOARD => "WM_DRAWCLIPBOARD",
                WM_CHANGECBCHAIN => "WM_CHANGECBCHAIN",
                _ when msg >= WM_DDE_FIRST && msg <= WM_DDE_LAST => $"WM_DDE_{msg - WM_DDE_FIRST}",
                _ when msg >= WM_USER => $"WM_USER+{msg - WM_USER}",
                _ => "Unknown",
            };
        }

        private readonly IntPtr _hwnd;
        private readonly Action<string[]> _callback;
        private readonly IntPtr _oldWndProc;
        private readonly WndProcDelegate _procDelegate;

        public DragDropHook(IntPtr hwnd, Action<string[]> callback)
        {
            _hwnd = hwnd;
            _callback = callback;
            _procDelegate = WndProc;
            _oldWndProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_procDelegate));

            if (DiagnosticsEnabled)
            {
                Debug.WriteLine($"[AdminDragDropFix] WndProc hook installed on window 0x{hwnd:X}");
            }
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            // Log all drag-and-drop related messages for diagnostics
            if (DiagnosticsEnabled)
            {
                if (msg == WM_DROPFILES || msg == WM_COPYDATA || msg == WM_COPYGLOBALDATA ||
                    msg == WM_GETOBJECT || msg == WM_DRAWCLIPBOARD || msg == WM_CHANGECBCHAIN ||
                    (msg >= WM_DDE_FIRST && msg <= WM_DDE_LAST))
                {
                    Debug.WriteLine($"[AdminDragDropFix] Received message: 0x{msg:X4} ({GetMessageName(msg)})");
                }
            }

            if (msg == WM_DROPFILES)
            {
                if (DiagnosticsEnabled)
                {
                    Debug.WriteLine($"[AdminDragDropFix] Handling WM_DROPFILES, hDrop=0x{wParam:X}");
                }

                HandleDrop(wParam);
                return IntPtr.Zero;
            }

            return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
        }

        private unsafe void HandleDrop(IntPtr hDrop)
        {
            try
            {
                uint count = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);

                if (DiagnosticsEnabled)
                {
                    Debug.WriteLine($"[AdminDragDropFix] Drop contains {count} file(s)");
                }

                var files = new List<string>();
                for (uint i = 0; i < count; i++)
                {
                    uint size = DragQueryFile(hDrop, i, null, 0);
                    if (size == 0)
                    {
                        continue;
                    }

                    var buffer = new char[(int)size + 1];
                    fixed (char* pBuffer = buffer)
                    {
                        uint result = DragQueryFile(hDrop, i, pBuffer, (uint)buffer.Length);
                        if (result > 0)
                        {
                            string path = new(buffer, 0, (int)result);
                            files.Add(path);

                            if (DiagnosticsEnabled)
                            {
                                Debug.WriteLine($"[AdminDragDropFix]   File {i + 1}: {path}");
                            }
                        }
                    }
                }

                if (files.Count > 0)
                {
                    _callback([.. files]);

                    if (DiagnosticsEnabled)
                    {
                        Debug.WriteLine($"[AdminDragDropFix] Invoked callback with {files.Count} file(s)");
                    }
                }
            }
            catch (Exception ex)
            {
                if (DiagnosticsEnabled)
                {
                    Debug.WriteLine($"[AdminDragDropFix] Error handling drop: {ex}");
                }
            }
            finally
            {
                DragFinish(hDrop);
            }
        }
    }

    // Keep hooks alive to prevent GC of the delegate
    private static readonly ConditionalWeakTable<Window, DragDropHook> _hooks = [];

    /// <summary>
    /// Applies the UIPI bypass to enable drag and drop for an elevated window.
    /// Optionally registers a callback to handle WM_DROPFILES directly, useful if the framework's OLE-based
    /// drag and drop is blocked by UIPI even with message filtering.
    /// </summary>
    /// <param name="window">The window to enable drag-and-drop for.</param>
    /// <param name="onDrop">Optional callback to handle dropped files manually via WM_DROPFILES.</param>
    /// <returns>True if the fix was successfully applied, false otherwise.</returns>
    public static bool Apply(Window window, Action<string[]>? onDrop = null)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        if (window.TryGetPlatformHandle() is not { Handle: { } hwnd })
        {
            if (DiagnosticsEnabled)
            {
                Debug.WriteLine("[AdminDragDropFix] Failed to get window handle");
            }

            return false;
        }

        if (DiagnosticsEnabled)
        {
            Debug.WriteLine($"[AdminDragDropFix] Applying fix to window 0x{hwnd:X}");
        }

        // Forcefully revoke OLE drop target to allow WM_DROPFILES to work
        try
        {
            int hr = RevokeDragDrop(hwnd);
            if (hr != 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            if (DiagnosticsEnabled)
            {
                Debug.WriteLine("[AdminDragDropFix] RevokeDragDrop called successfully");
            }
        }
        catch (Exception ex)
        {
            if (DiagnosticsEnabled)
            {
                Debug.WriteLine($"[AdminDragDropFix] RevokeDragDrop failed (ignorable): {ex.Message}");
            }
        }

        var filter = new CHANGEFILTERSTRUCT { CbSize = (uint)Marshal.SizeOf<CHANGEFILTERSTRUCT>() };

        // Allow standard drag-and-drop messages
        (uint, string)[] messages =
        [
            (WM_DROPFILES, "WM_DROPFILES"),
            (WM_COPYDATA, "WM_COPYDATA"),
            (WM_COPYGLOBALDATA, "WM_COPYGLOBALDATA"),
            (WM_GETOBJECT, "WM_GETOBJECT"),
            (WM_DRAWCLIPBOARD, "WM_DRAWCLIPBOARD"),
            (WM_CHANGECBCHAIN, "WM_CHANGECBCHAIN"),
        ];

        bool allSuccess = true;
        foreach (var (msg, name) in messages)
        {
            bool result = ChangeWindowMessageFilterEx(hwnd, msg, MSGFLT_ALLOW, ref filter);

            if (DiagnosticsEnabled)
            {
                Debug.WriteLine($"[AdminDragDropFix] ChangeWindowMessageFilterEx({name}): {(result ? "SUCCESS" : "FAILED")}");
                if (!result)
                {
                    int error = Marshal.GetLastWin32Error();
                    Debug.WriteLine($"[AdminDragDropFix]   Last Win32 Error: {error}");
                }
            }

            allSuccess &= result;
        }

        // Enable the window to accept dropped files
        DragAcceptFiles(hwnd, true);

        if (DiagnosticsEnabled)
        {
            Debug.WriteLine("[AdminDragDropFix] DragAcceptFiles(true) called");
        }

        // Install WndProc hook if callback provided
        if (onDrop != null)
        {
            if (!_hooks.TryGetValue(window, out _))
            {
                var hook = new DragDropHook(hwnd, onDrop);
                _hooks.Add(window, hook);

                if (DiagnosticsEnabled)
                {
                    Debug.WriteLine("[AdminDragDropFix] WndProc hook registered");
                }
            }
        }

        if (DiagnosticsEnabled)
        {
            Debug.WriteLine($"[AdminDragDropFix] Fix application {(allSuccess ? "completed successfully" : "completed with some failures")}");
        }

        return allSuccess;
    }
}
