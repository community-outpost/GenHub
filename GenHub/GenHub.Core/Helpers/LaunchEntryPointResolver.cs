using System;
using System.IO;
using GenHub.Core.Constants;

namespace GenHub.Core.Helpers;

/// <summary>
/// Relates a launch entry point to the process that ends up owning the game session.
/// </summary>
public static class LaunchEntryPointResolver
{
    /// <summary>
    /// Resolves the process that <paramref name="executablePath"/> is expected to spawn and hand
    /// the session to.
    /// </summary>
    /// <param name="executablePath">The executable being launched.</param>
    /// <returns>
    /// The expected child process name without extension, or <see langword="null"/> when the
    /// launched executable is itself the game.
    /// </returns>
    public static string? ResolveExpectedChildProcessName(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath))
        {
            return null;
        }

        var fileName = Path.GetFileName(executablePath);
        if (fileName.Equals(GameClientConstants.GeneralsOnlineEacLauncherExecutable, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileNameWithoutExtension(GameClientConstants.GeneralsOnline60HzExecutable);
        }

        return null;
    }
}
