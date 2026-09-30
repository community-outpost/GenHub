using GenHub.Core.Constants;
using System;
using System.IO;

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
        var childFileName = ResolveExpectedChildFileName(executablePath);
        return childFileName is null ? null : Path.GetFileNameWithoutExtension(childFileName);
    }

    /// <summary>
    /// Resolves the file name of the binary that <paramref name="executablePath"/> is expected to
    /// spawn and hand the session to. It sits next to the launched executable.
    /// </summary>
    /// <param name="executablePath">The executable being launched.</param>
    /// <returns>
    /// The expected child's file name, or <see langword="null"/> when the launched executable is
    /// itself the game.
    /// </returns>
    public static string? ResolveExpectedChildFileName(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath))
        {
            return null;
        }

        var fileName = Path.GetFileName(executablePath);
        if (fileName.Equals(GameClientConstants.GeneralsOnlineEacLauncherExecutable, StringComparison.OrdinalIgnoreCase))
        {
            return GameClientConstants.GeneralsOnline60HzExecutable;
        }

        if (fileName.Equals(GameClientConstants.GeneralsExecutable, StringComparison.OrdinalIgnoreCase))
        {
            var directory = Path.GetDirectoryName(executablePath);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                var gameDat = Path.Combine(directory, GameClientConstants.SteamGameDatExecutable);
                if (!File.Exists(gameDat))
                {
                    return null;
                }
            }

            return GameClientConstants.SteamGameDatExecutable;
        }

        return null;
    }
}
