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
    internal static IntPtr GetClass(string name) => GetClassNative(name);

    /// <summary>Calls <c>sel_registerName</c>.</summary>
    /// <param name="name">The selector name.</param>
    /// <returns>The native result.</returns>
    internal static IntPtr RegisterSelector(string name) => RegisterSelectorNative(name);

    /// <summary>Calls <c>object_getClass</c>.</summary>
    /// <param name="obj">The object.</param>
    /// <returns>The native result.</returns>
    internal static IntPtr GetObjectClass(IntPtr obj) => GetObjectClassNative(obj);

    /// <summary>Calls <c>class_getInstanceMethod</c>.</summary>
    /// <param name="cls">The class.</param>
    /// <param name="selector">The selector.</param>
    /// <returns>The native result.</returns>
    internal static IntPtr GetInstanceMethod(IntPtr cls, IntPtr selector) => GetInstanceMethodNative(cls, selector);

    /// <summary>Calls <c>method_getImplementation</c>.</summary>
    /// <param name="method">The method.</param>
    /// <returns>The native result.</returns>
    internal static IntPtr GetMethodImplementation(IntPtr method) => GetMethodImplementationNative(method);

    /// <summary>Calls <c>method_getTypeEncoding</c>.</summary>
    /// <param name="method">The method.</param>
    /// <returns>The native result.</returns>
    internal static IntPtr GetMethodTypeEncoding(IntPtr method) => GetMethodTypeEncodingNative(method);

    /// <summary>Calls <c>class_replaceMethod</c>.</summary>
    /// <param name="cls">The class.</param>
    /// <param name="selector">The selector.</param>
    /// <param name="implementation">The new implementation.</param>
    /// <param name="types">The method type encoding.</param>
    /// <returns>The native result.</returns>
    internal static IntPtr ReplaceMethod(IntPtr cls, IntPtr selector, IntPtr implementation, IntPtr types) => ReplaceMethodNative(cls, selector, implementation, types);

    /// <summary>Sends a message that returns <see cref="IntPtr"/>.</summary>
    /// <param name="receiver">The message receiver.</param>
    /// <param name="selector">The selector.</param>
    /// <returns>The native result.</returns>
    internal static IntPtr SendIntPtr(IntPtr receiver, IntPtr selector) => SendIntPtrNative(receiver, selector);

    /// <summary>Sends a message that returns <see cref="bool"/>.</summary>
    /// <param name="receiver">The message receiver.</param>
    /// <param name="selector">The selector.</param>
    /// <returns>The native result.</returns>
    internal static bool SendBool(IntPtr receiver, IntPtr selector) => SendBoolNative(receiver, selector);

    /// <summary>Sends a message that returns <see cref="int"/>.</summary>
    /// <param name="receiver">The message receiver.</param>
    /// <param name="selector">The selector.</param>
    /// <returns>The native result.</returns>
    internal static int SendInt32(IntPtr receiver, IntPtr selector) => SendInt32Native(receiver, selector);

    /// <summary>Sends a message that returns <see cref="ulong"/>.</summary>
    /// <param name="receiver">The message receiver.</param>
    /// <param name="selector">The selector.</param>
    /// <returns>The native result.</returns>
    internal static ulong SendUInt64(IntPtr receiver, IntPtr selector) => SendUInt64Native(receiver, selector);

    /// <summary>Sends a message that returns <see cref="double"/>.</summary>
    /// <param name="receiver">The message receiver.</param>
    /// <param name="selector">The selector.</param>
    /// <returns>The native result.</returns>
    internal static double SendDouble(IntPtr receiver, IntPtr selector) => SendDoubleNative(receiver, selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_getClass")]
    private static extern IntPtr GetClassNative(string name);

    [DllImport(ObjCLibrary, EntryPoint = "sel_registerName")]
    private static extern IntPtr RegisterSelectorNative(string name);

    [DllImport(ObjCLibrary, EntryPoint = "object_getClass")]
    private static extern IntPtr GetObjectClassNative(IntPtr obj);

    [DllImport(ObjCLibrary, EntryPoint = "class_getInstanceMethod")]
    private static extern IntPtr GetInstanceMethodNative(IntPtr cls, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "method_getImplementation")]
    private static extern IntPtr GetMethodImplementationNative(IntPtr method);

    [DllImport(ObjCLibrary, EntryPoint = "method_getTypeEncoding")]
    private static extern IntPtr GetMethodTypeEncodingNative(IntPtr method);

    [DllImport(ObjCLibrary, EntryPoint = "class_replaceMethod")]
    private static extern IntPtr ReplaceMethodNative(IntPtr cls, IntPtr selector, IntPtr implementation, IntPtr types);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIntPtrNative(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendBoolNative(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern int SendInt32Native(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern ulong SendUInt64Native(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern double SendDoubleNative(IntPtr receiver, IntPtr selector);
}
