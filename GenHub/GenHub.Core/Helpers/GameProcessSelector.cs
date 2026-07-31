using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenHub.Core.Constants;
using GenHub.Core.Models.Launching;

namespace GenHub.Core.Helpers;

/// <summary>
/// Decides which running process is the game a launch spawned.
/// </summary>
public static class GameProcessSelector
{
    /// <summary>
    /// Selects the process matching <paramref name="processName"/> that this launch spawned.
    /// </summary>
    /// <param name="candidates">The processes currently observed on the machine.</param>
    /// <param name="processName">The expected process name, without extension.</param>
    /// <param name="workingDirectory">The directory the game must run from, or <see langword="null"/> to skip the check.</param>
    /// <param name="now">The current time, used to apply the recency window.</param>
    /// <returns>The selected candidate, or <see langword="null"/> when none qualifies.</returns>
    public static GameProcessCandidate? SelectSpawnedGameProcess(
        IEnumerable<GameProcessCandidate> candidates,
        string processName,
        string? workingDirectory,
        DateTime now)
    {
        var matches = candidates
            .Where(candidate => candidate.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
            .Where(candidate => (now - candidate.StartTime).TotalSeconds < ProcessConstants.EarlyExitThresholdSeconds);

        // Residence is required whenever a working directory is known, including for a lone match:
        // a same-named process elsewhere on the machine is somebody else's.
        if (!string.IsNullOrEmpty(workingDirectory))
        {
            matches = matches.Where(candidate => ResidesIn(candidate, workingDirectory));
        }

        return matches
            .OrderByDescending(candidate => candidate.StartTime)
            .FirstOrDefault();
    }

    private static bool ResidesIn(GameProcessCandidate candidate, string workingDirectory)
    {
        if (candidate.ExecutablePath is null)
        {
            return false;
        }

        var directory = Path.GetDirectoryName(candidate.ExecutablePath);
        return directory != null && Normalize(directory).Equals(Normalize(workingDirectory), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
