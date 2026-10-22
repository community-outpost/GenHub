using GenHub.Core.Interfaces.Common;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static GenHub.MacOS.Infrastructure.AppActivation.ObjCRuntime;

namespace GenHub.MacOS.Infrastructure.AppActivation;

/// <summary>
/// Tells <see cref="ILinkActivationTracker"/> when AppKit has finished launching GenHub.
/// </summary>
/// <remarks>
/// Apple documents that a file the app was launched to open reaches <c>application:openFile:</c> before
/// <c>applicationDidFinishLaunching:</c> (https://developer.apple.com/documentation/appkit/nsapplicationdelegate/applicationdidfinishlaunching(_:)).
/// A genhub:// link that cold starts GenHub takes the same path: its GURL Apple Event reaches
/// <c>application:openURLs:</c> before <c>applicationDidFinishLaunching:</c>, verified on a packaged build.
/// Once launching finishes, the tracker knows whether the session was opened for a link.
/// </remarks>
[SupportedOSPlatform("macos")]
internal static class MacLaunchCompletionHook
{
    private static DidFinishLaunchingHandler? _originalDidFinishLaunching;
    private static DidFinishLaunchingHandler? _didFinishLaunchingHook;
    private static ILinkActivationTracker? _tracker;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DidFinishLaunchingHandler(IntPtr self, IntPtr selector, IntPtr notification);

    /// <summary>
    /// Hooks the Avalonia application delegate so the tracker learns when launching finishes.
    /// When the hook cannot be installed, launching is marked finished at once so startup never waits on it.
    /// </summary>
    /// <param name="tracker">The tracker to notify.</param>
    /// <param name="logger">The logger for installation problems.</param>
    /// <returns><see langword="true"/> when the hook was installed.</returns>
    internal static bool Install(ILinkActivationTracker tracker, ILogger? logger)
    {
        if (_didFinishLaunchingHook != null)
        {
            return false;
        }

        try
        {
            var application = GetSharedApplication();
            if (application == IntPtr.Zero || SendBool(application, RegisterSelector("isRunning")))
            {
                logger?.LogWarning("AppKit is unavailable or already running; not waiting for the launch to finish");
                tracker.MarkLaunchFinished();
                return false;
            }

            var applicationDelegate = SendIntPtr(application, RegisterSelector("delegate"));
            var delegateClass = applicationDelegate == IntPtr.Zero ? IntPtr.Zero : GetObjectClass(applicationDelegate);
            var selector = RegisterSelector("applicationDidFinishLaunching:");
            var method = delegateClass == IntPtr.Zero ? IntPtr.Zero : GetInstanceMethod(delegateClass, selector);
            if (method == IntPtr.Zero)
            {
                logger?.LogWarning("Could not locate the application delegate's launch callback; not waiting for the launch to finish");
                tracker.MarkLaunchFinished();
                return false;
            }

            _tracker = tracker;
            _originalDidFinishLaunching = Marshal.GetDelegateForFunctionPointer<DidFinishLaunchingHandler>(GetMethodImplementation(method));
            _didFinishLaunchingHook = OnDidFinishLaunching;
            ReplaceMethod(delegateClass, selector, Marshal.GetFunctionPointerForDelegate(_didFinishLaunchingHook), GetMethodTypeEncoding(method));
            return true;
        }
        catch (Exception ex)
        {
            // Keep delegates rooted: an exception after replacement must not invalidate native callbacks.
            logger?.LogWarning(ex, "Failed to hook the macOS launch callback; not waiting for the launch to finish");
            tracker.MarkLaunchFinished();
            return false;
        }
    }

    private static void OnDidFinishLaunching(IntPtr self, IntPtr selector, IntPtr notification)
    {
        try
        {
            _originalDidFinishLaunching?.Invoke(self, selector, notification);
        }
        finally
        {
            _tracker?.MarkLaunchFinished();
        }
    }
}
