using GenHub.Common.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static GenHub.MacOS.Infrastructure.AppActivation.ObjCRuntime;

namespace GenHub.MacOS.Infrastructure.AppActivation;

/// <summary>
/// Stops Avalonia from forcing GenHub to the front on macOS.
/// </summary>
/// <remarks>
/// Avalonia.Native activates the app when launching finishes (through
/// <c>-[NSRunningApplication activateWithOptions:]</c>), when any window is shown,
/// and when a modal dialog closes. That pulls a background launch (<c>open -g</c>) or a
/// dialog opened while the user works elsewhere in front of the user's current app.
/// For a bundled app this gate lets LaunchServices decide launch activation and only
/// passes activation requests through while GenHub is already active, while the user is
/// interacting with it, or while GenHub handles a request that targets it such as a link.
/// </remarks>
[SupportedOSPlatform("macos")]
internal static class MacAppActivationGate
{
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

    private static ActivateIgnoringOtherAppsHandler? _originalActivateIgnoringOtherApps;
    private static ActivateIgnoringOtherAppsHandler? _activateIgnoringOtherAppsHook;
    private static ActivateHandler? _originalActivate;
    private static ActivateHandler? _activateHook;
    private static ActivateWithOptionsHandler? _originalActivateWithOptions;
    private static ActivateWithOptionsHandler? _activateWithOptionsHook;
    private static IntPtr _application;
    private static ILogger? _logger;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ActivateIgnoringOtherAppsHandler(IntPtr self, IntPtr selector, byte ignoringOtherApps);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ActivateHandler(IntPtr self, IntPtr selector);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte ActivateWithOptionsHandler(IntPtr self, IntPtr selector, nuint options);

    /// <summary>
    /// Installs the gate when GenHub runs from an app bundle. Unbundled development runs keep
    /// Avalonia's behaviour because nothing else brings a terminal-launched process forward.
    /// </summary>
    /// <param name="processPath">The path of the running executable.</param>
    /// <param name="logger">The logger for installation and blocked activations.</param>
    /// <returns><see langword="true"/> when the gate was installed.</returns>
    internal static bool Install(string? processPath, ILogger? logger)
    {
        if (_activateIgnoringOtherAppsHook != null || !IsRunningFromAppBundle(processPath))
        {
            return false;
        }

        _logger = logger;
        try
        {
            var application = GetSharedApplication();
            var activateIgnoringOtherAppsSelector = RegisterSelector("activateIgnoringOtherApps:");
            var applicationClass = GetObjectClass(application);
            var activateIgnoringOtherAppsMethod = GetInstanceMethod(applicationClass, activateIgnoringOtherAppsSelector);
            var runningApplicationClass = GetClass("NSRunningApplication");
            var activateWithOptionsSelector = RegisterSelector("activateWithOptions:");
            var activateWithOptionsMethod = GetInstanceMethod(runningApplicationClass, activateWithOptionsSelector);
            if (application == IntPtr.Zero || activateIgnoringOtherAppsMethod == IntPtr.Zero || activateWithOptionsMethod == IntPtr.Zero)
            {
                logger?.LogWarning("Could not locate the Avalonia activation methods; app activation is not gated");
                return false;
            }

            _application = application;
            _originalActivateIgnoringOtherApps = Marshal.GetDelegateForFunctionPointer<ActivateIgnoringOtherAppsHandler>(GetMethodImplementation(activateIgnoringOtherAppsMethod));
            _activateIgnoringOtherAppsHook = OnActivateIgnoringOtherApps;
            _originalActivateWithOptions = Marshal.GetDelegateForFunctionPointer<ActivateWithOptionsHandler>(GetMethodImplementation(activateWithOptionsMethod));
            _activateWithOptionsHook = OnActivateWithOptions;

            // macOS 14 added -[NSApplication activate]; older systems do not have it.
            var activateSelector = RegisterSelector("activate");
            var activateMethod = GetInstanceMethod(applicationClass, activateSelector);
            if (activateMethod != IntPtr.Zero)
            {
                _originalActivate = Marshal.GetDelegateForFunctionPointer<ActivateHandler>(GetMethodImplementation(activateMethod));
                _activateHook = OnActivate;
            }

            ReplaceMethod(
                applicationClass,
                activateIgnoringOtherAppsSelector,
                Marshal.GetFunctionPointerForDelegate(_activateIgnoringOtherAppsHook),
                GetMethodTypeEncoding(activateIgnoringOtherAppsMethod));
            ReplaceMethod(
                runningApplicationClass,
                activateWithOptionsSelector,
                Marshal.GetFunctionPointerForDelegate(_activateWithOptionsHook),
                GetMethodTypeEncoding(activateWithOptionsMethod));
            if (_activateHook != null)
            {
                ReplaceMethod(
                    applicationClass,
                    activateSelector,
                    Marshal.GetFunctionPointerForDelegate(_activateHook),
                    GetMethodTypeEncoding(activateMethod));
            }

            logger?.LogInformation("Installed the macOS app activation gate");
            return true;
        }
        catch (Exception ex)
        {
            // Keep delegates rooted: an exception after replacement must not invalidate native callbacks.
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
    /// Determines whether an <c>NSRunningApplication</c> activation targets GenHub and so must pass the gate.
    /// </summary>
    /// <param name="targetProcessId">The process identifier of the application being activated.</param>
    /// <param name="currentProcessId">The process identifier of GenHub.</param>
    /// <returns><see langword="true"/> when the request activates GenHub itself.</returns>
    internal static bool IsSelfActivation(int targetProcessId, int currentProcessId) =>
        targetProcessId == currentProcessId;

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
        if (IsActivationAllowed(self))
        {
            _originalActivateIgnoringOtherApps?.Invoke(self, selector, ignoringOtherApps);
        }
    }

    private static void OnActivate(IntPtr self, IntPtr selector)
    {
        if (IsActivationAllowed(self))
        {
            _originalActivate?.Invoke(self, selector);
        }
    }

    private static byte OnActivateWithOptions(IntPtr self, IntPtr selector, nuint options)
    {
        if (TargetsCurrentProcess(self) && !IsActivationAllowed(_application))
        {
            return 0;
        }

        return _originalActivateWithOptions?.Invoke(self, selector, options) ?? 0;
    }

    private static bool TargetsCurrentProcess(IntPtr runningApplication)
    {
        try
        {
            return IsSelfActivation(SendInt32(runningApplication, RegisterSelector("processIdentifier")), Environment.ProcessId);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "App activation target check failed; allowing activation");
            return false;
        }
    }

    private static bool IsActivationAllowed(IntPtr application)
    {
        try
        {
            if (!ShouldAllowActivation(IsAppActive(application), HasRecentUserInput(application), WindowActivation.IsUserRequestInProgress))
            {
                _logger?.LogDebug("Blocked an app activation that GenHub did not request");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "App activation check failed; allowing activation");
        }

        return true;
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
}
