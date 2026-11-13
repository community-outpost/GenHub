using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GenHub.MacOS.Infrastructure.AppActivation;

/// <summary>
/// Objective-C runtime calls used to observe and gate AppKit behaviour from managed code.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class ObjCRuntime
{
    private const string ObjCLibrary = "/usr/lib/libobjc.A.dylib";

    /// <summary>
    /// Gets the shared <c>NSApplication</c> instance.
    /// </summary>
    /// <returns>The application object, or <see cref="IntPtr.Zero"/> when AppKit is unavailable.</returns>
    internal static IntPtr GetSharedApplication() =>
        SendIntPtr(GetClass("NSApplication"), RegisterSelector("sharedApplication"));

    /// <summary>Calls <c>objc_getClass</c>.</summary>
    /// <param name="name">The class name.</param>
    /// <returns>The native result.</returns>
    [DllImport(ObjCLibrary, EntryPoint = "objc_getClass")]
    internal static extern IntPtr GetClass(string name);

    /// <summary>Calls <c>sel_registerName</c>.</summary>
    /// <param name="name">The selector name.</param>
    /// <returns>The native result.</returns>
    [DllImport(ObjCLibrary, EntryPoint = "sel_registerName")]
    internal static extern IntPtr RegisterSelector(string name);

    /// <summary>Calls <c>object_getClass</c>.</summary>
    /// <param name="obj">The object.</param>
    /// <returns>The native result.</returns>
    [DllImport(ObjCLibrary, EntryPoint = "object_getClass")]
    internal static extern IntPtr GetObjectClass(IntPtr obj);

    /// <summary>Calls <c>class_getInstanceMethod</c>.</summary>
    /// <param name="cls">The class.</param>
    /// <param name="selector">The selector.</param>
    /// <returns>The native result.</returns>
    [DllImport(ObjCLibrary, EntryPoint = "class_getInstanceMethod")]
    internal static extern IntPtr GetInstanceMethod(IntPtr cls, IntPtr selector);

    /// <summary>Calls <c>method_getImplementation</c>.</summary>
    /// <param name="method">The method.</param>
    /// <returns>The native result.</returns>
    [DllImport(ObjCLibrary, EntryPoint = "method_getImplementation")]
    internal static extern IntPtr GetMethodImplementation(IntPtr method);

    /// <summary>Calls <c>method_getTypeEncoding</c>.</summary>
    /// <param name="method">The method.</param>
    /// <returns>The native result.</returns>
    [DllImport(ObjCLibrary, EntryPoint = "method_getTypeEncoding")]
    internal static extern IntPtr GetMethodTypeEncoding(IntPtr method);

    /// <summary>Calls <c>class_replaceMethod</c>.</summary>
    /// <param name="cls">The class.</param>
    /// <param name="selector">The selector.</param>
    /// <param name="implementation">The new implementation.</param>
    /// <param name="types">The method type encoding.</param>
    /// <returns>The native result.</returns>
    [DllImport(ObjCLibrary, EntryPoint = "class_replaceMethod")]
    internal static extern IntPtr ReplaceMethod(IntPtr cls, IntPtr selector, IntPtr implementation, IntPtr types);

    /// <summary>Sends a message that returns <see cref="IntPtr"/>.</summary>
    /// <param name="receiver">The message receiver.</param>
    /// <param name="selector">The selector.</param>
    /// <returns>The native result.</returns>
    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    internal static extern IntPtr SendIntPtr(IntPtr receiver, IntPtr selector);

    /// <summary>Sends a message that returns <see cref="bool"/>.</summary>
    /// <param name="receiver">The message receiver.</param>
    /// <param name="selector">The selector.</param>
    /// <returns>The native result.</returns>
    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SendBool(IntPtr receiver, IntPtr selector);

    /// <summary>Sends a message that returns <see cref="int"/>.</summary>
    /// <param name="receiver">The message receiver.</param>
    /// <param name="selector">The selector.</param>
    /// <returns>The native result.</returns>
    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    internal static extern int SendInt32(IntPtr receiver, IntPtr selector);

    /// <summary>Sends a message that returns <see cref="ulong"/>.</summary>
    /// <param name="receiver">The message receiver.</param>
    /// <param name="selector">The selector.</param>
    /// <returns>The native result.</returns>
    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    internal static extern ulong SendUInt64(IntPtr receiver, IntPtr selector);

    /// <summary>Sends a message that returns <see cref="double"/>.</summary>
    /// <param name="receiver">The message receiver.</param>
    /// <param name="selector">The selector.</param>
    /// <returns>The native result.</returns>
    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    internal static extern double SendDouble(IntPtr receiver, IntPtr selector);
}
