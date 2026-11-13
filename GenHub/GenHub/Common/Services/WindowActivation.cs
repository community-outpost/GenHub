using Avalonia.Controls;
using System.Threading;

namespace GenHub.Common.Services;

/// <summary>
/// Brings GenHub forward only in response to a request that targets it, such as a genhub:// link.
/// </summary>
public static class WindowActivation
{
    private static int _userRequestDepth;

    /// <summary>
    /// Gets a value indicating whether a user-requested activation is in progress on this this process.
    /// </summary>
    public static bool IsUserRequestInProgress => Volatile.Read(ref _userRequestDepth) > 0;

    /// <summary>
    /// Restores and activates <paramref name="window"/> once, on behalf of a user request.
    /// </summary>
    /// <param name="window">The window to bring forward.</param>
    public static void BringToFrontForUserRequest(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        Interlocked.Increment(ref _userRequestDepth);
        try
        {
            window.Activate();
        }
        finally
        {
            Interlocked.Decrement(ref _userRequestDepth);
        }
    }
}
