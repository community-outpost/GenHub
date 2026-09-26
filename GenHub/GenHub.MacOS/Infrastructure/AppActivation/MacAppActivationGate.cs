using GenHub.Common.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GenHub.MacOS.Infrastructure.AppActivation;

/// <summary>
/// Stops Avalonia from forcing GenHub to the front on macOS.
/// </summary>
/// <remarks>
/// Avalonia.Native activates the app when launching finishes, when any window is shown,
/// and when a modal dialog closes. That pulls a background launch (<c>open -g</c>) or a
/// dialog opened while the user works elsewhere in front of the user's current app.
/// For a bundled app this gate lets LaunchServices decide launch activation and only
/// passes activation requests through while GenHub is already active, while the user is
/// interacting with it, or while GenHub handles a request that targets it such as a link.
/// </remarks>
[SupportedOSPlatform("macos")]
internal static class MacAppActivationGate
{
    private const string ObjCLibrary = "/usr/lib/libobjc.A.dylib";
    private const string AvaloniaAppDelegateClass = "AvnAppDelegate";
    private const string AppBundleExecutableMarker = ".app/Contents/MacOS/";
    private const double RecentInputWindowSeconds = 1.0;

    private const ulong LeftMouseDown = 1;
    private const ulong LeftMouseUp = 2;
    private const ulong RightMouseDown = 3;
    private const ulong RightMouseUp = 4;
    private const ulong KeyDown = 10;
    private const ulong KeyUp = 11;
    private const ulong OtherMouseDown = 25;
    private const ulong OtherMouseUp = 26;

    private static ActivateIgnoringOtherAppsHandler? _originalActivate;
    private static ActivateIgnoringOtherAppsHandler? _activateHook;
    private static NotificationHandler? _didFinishLaunchingHook;
    private static ILogger? _logger;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ActivateIgnoringOtherAppsHandler(IntPtr self, IntPtr selector, byte ignoringOtherApps);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NotificationHandler(IntPtr self, IntPtr selector, IntPtr notification);

    /// <summary>
    /// Installs the gate when GenHub runs from an app bundle. Unbundled development runs keep
    /// Avalonia's behaviour because nothing else brings a terminal-launched process forward.
    /// </summary>
    /// <param name="processPath">The path of the running executable.</param>
    /// <param name="logger">The logger for installation and blocked activations.</param>
    /// <returns><see langword="true"/> when the gate was installed.</returns>
    internal static bool Install(string? processPath, ILogger? logger)
    {
        if (_activateHook != null || !IsRunningFromAppBundle(processPath))
        {
            return false;
        }

        _logger = logger;
        try
        {
            var application = GetSharedApplication();
            var activateSelector = RegisterSelector("activateIgnoringOtherApps:");
            var applicationClass = GetObjectClass(application);
            var activateMethod = GetInstanceMethod(applicationClass, activateSelector);
            var delegateClass = GetClass(AvaloniaAppDelegateClass);
            var didFinishSelector = RegisterSelector("applicationDidFinishLaunching:");
            var didFinishMethod = delegateClass == IntPtr.Zero ? IntPtr.Zero : GetInstanceMethod(delegateClass, didFinishSelector);
            if (application == IntPtr.Zero || activateMethod == IntPtr.Zero || didFinishMethod == IntPtr.Zero)
            {
                logger?.LogWarning("Could not locate the Avalonia activation methods; app activation is not gated");
                return false;
            }

            _originalActivate = Marshal.GetDelegateForFunctionPointer<ActivateIgnoringOtherAppsHandler>(GetMethodImplementation(activateMethod));
            _activateHook = OnActivateIgnoringOtherApps;
            _didFinishLaunchingHook = OnDidFinishLaunching;

            ReplaceMethod(
                applicationClass,
                activateSelector,
                Marshal.GetFunctionPointerForDelegate(_activateHook),
                GetMethodTypeEncoding(activateMethod));
            ReplaceMethod(
                delegateClass,
                didFinishSelector,
                Marshal.GetFunctionPointerForDelegate(_didFinishLaunchingHook),
                GetMethodTypeEncoding(didFinishMethod));

            logger?.LogInformation("Installed the macOS app activation gate");
            return true;
        }
        catch (Exception ex)
        {
            _originalActivate = null;
            _activateHook = null;
            _didFinishLaunchingHook = null;
            logger?.LogWarning(ex, "Failed to install the macOS app activation gate");
            return false;
        }
    }

    /// <summary>
    /// Decides whether an activation request may bring GenHub to the front.
    /// </summary>
    /// <param name="isAppActive">Whether GenHub is already the active app.</param>
    /// <param name="isRecentUserInput">Whether the request comes from input the user just sent to GenHub.</param>
    /// <param name="isUserRequestInProgress">Whether GenHub is handling a request that targets it, such as a link.</param>
    /// <returns><see langword="true"/> when the activation may proceed.</returns>
    internal static bool ShouldAllowActivation(bool isAppActive, bool isRecentUserInput, bool isUserRequestInProgress) =>
        isAppActive || isRecentUserInput || isUserRequestInProgress;

    /// <summary>
    /// Determines whether an event is mouse or keyboard input sent within the last second.
    /// </summary>
    /// <param name="eventType">The AppKit event type.</param>
    /// <param name="eventTimestamp">The event timestamp in seconds since system startup.</param>
    /// <param name="systemUptime">The current time in seconds since system startup.</param>
    /// <returns><see langword="true"/> for recent user input.</returns>
    internal static bool IsRecentUserInput(ulong eventType, double eventTimestamp, double systemUptime)
    {
        var isInput = eventType is LeftMouseDown or LeftMouseUp or RightMouseDown or RightMouseUp
            or KeyDown or KeyUp or OtherMouseDown or OtherMouseUp;
        var age = systemUptime - eventTimestamp;
        return isInput && age >= 0 && age <= RecentInputWindowSeconds;
    }

    /// <summary>
    /// Determines whether the executable runs from inside an <c>.app</c> bundle.
    /// </summary>
    /// <param name="processPath">The path of the running executable.</param>
    /// <returns><see langword="true"/> for a bundled executable.</returns>
    internal static bool IsRunningFromAppBundle(string? processPath) =>
        !string.IsNullOrEmpty(processPath) && processPath.Contains(AppBundleExecutableMarker, StringComparison.Ordinal);

    private static void OnActivateIgnoringOtherApps(IntPtr self, IntPtr selector, byte ignoringOtherApps)
    {
        try
        {
            if (!ShouldAllowActivation(IsAppActive(self), HasRecentUserInput(self), WindowActivation.IsUserRequestInProgress))
            {
                _logger?.LogDebug("Blocked an app activation that GenHub did not request");
                return;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "App activation check failed; allowing activation");
        }

        _originalActivate?.Invoke(self, selector, ignoringOtherApps);
    }

    private static void OnDidFinishLaunching(IntPtr self, IntPtr selector, IntPtr notification)
    {
        _logger?.LogDebug("Left launch activation to LaunchServices");
    }

    private static bool IsAppActive(IntPtr application) =>
        SendBool(application, RegisterSelector("isActive"));

    private static bool HasRecentUserInput(IntPtr application)
    {
        var currentEvent = SendIntPtr(application, RegisterSelector("currentEvent"));
        if (currentEvent == IntPtr.Zero)
        {
            return false;
        }

        var processInfo = SendIntPtr(GetClass("NSProcessInfo"), RegisterSelector("processInfo"));
        return IsRecentUserInput(
            SendUInt64(currentEvent, RegisterSelector("type")),
            SendDouble(currentEvent, RegisterSelector("timestamp")),
            SendDouble(processInfo, RegisterSelector("systemUptime")));
    }

    private static IntPtr GetSharedApplication() =>
        SendIntPtr(GetClass("NSApplication"), RegisterSelector("sharedApplication"));

    [DllImport(ObjCLibrary, EntryPoint = "objc_getClass")]
    private static extern IntPtr GetClass(string name);

    [DllImport(ObjCLibrary, EntryPoint = "sel_registerName")]
    private static extern IntPtr RegisterSelector(string name);

    [DllImport(ObjCLibrary, EntryPoint = "object_getClass")]
    private static extern IntPtr GetObjectClass(IntPtr obj);

    [DllImport(ObjCLibrary, EntryPoint = "class_getInstanceMethod")]
    private static extern IntPtr GetInstanceMethod(IntPtr cls, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "method_getImplementation")]
    private static extern IntPtr GetMethodImplementation(IntPtr method);

    [DllImport(ObjCLibrary, EntryPoint = "method_getTypeEncoding")]
    private static extern IntPtr GetMethodTypeEncoding(IntPtr method);

    [DllImport(ObjCLibrary, EntryPoint = "class_replaceMethod")]
    private static extern IntPtr ReplaceMethod(IntPtr cls, IntPtr selector, IntPtr implementation, IntPtr types);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIntPtr(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendBool(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern ulong SendUInt64(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern double SendDouble(IntPtr receiver, IntPtr selector);
}
