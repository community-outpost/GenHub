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
    /// Gets the name to enumerate by when looking for <paramref name="processName"/>. Unix kernels
    /// keep only the first <see cref="ProcessConstants.UnixProcessNameMaxLength"/> characters of a
    /// process name, and <see cref="System.Diagnostics.Process.GetProcessesByName(string)"/> matches
    /// against that truncated value, so asking for a longer name finds nothing at all. Windows
    /// reports names in full and is asked for them unchanged.
    /// </summary>
    /// <param name="processName">The expected process name, without extension.</param>
    /// <returns>The name to ask the operating system for.</returns>
    public static string GetDiscoveryName(string processName)
    {
        if (OperatingSystem.IsWindows() || processName.Length <= ProcessConstants.UnixProcessNameMaxLength)
        {
            return processName;
        }

        return processName[..ProcessConstants.UnixProcessNameMaxLength];
    }

    /// <summary>
    /// Selects the process matching <paramref name="processName"/> that this launch spawned.
    /// </summary>
    /// <param name="candidates">The processes currently observed on the machine. Each candidate's <see cref="GameProcessCandidate.StartTime"/> must be a UTC <see cref="DateTime"/> with <see cref="DateTimeKind.Utc"/>.</param>
    /// <param name="processName">The expected process name, without extension.</param>
    /// <param name="workingDirectory">The directory the game must run from, or <see langword="null"/> to skip the check.</param>
    /// <param name="now">The current time, used to apply the recency window. Must be a UTC <see cref="DateTime"/> with <see cref="DateTimeKind.Utc"/>.</param>
    /// <param name="launcherStartTime">The start time of the launcher process, if known. Must be a UTC <see cref="DateTime"/> with <see cref="DateTimeKind.Utc"/> when supplied.</param>
    /// <returns>The selected candidate, or <see langword="null"/> when none qualifies.</returns>
    public static GameProcessCandidate? SelectSpawnedGameProcess(
        IEnumerable<GameProcessCandidate> candidates,
        string processName,
        string? workingDirectory,
        DateTime now,
        DateTime? launcherStartTime = null)
    {
        var matches = candidates
            .Where(candidate => NameMatches(candidate, processName))
            .Where(candidate => (now - candidate.StartTime).TotalSeconds < ProcessConstants.EarlyExitThresholdSeconds);

        if (launcherStartTime.HasValue)
        {
            matches = matches.Where(candidate => candidate.StartTime >= launcherStartTime.Value);
        }

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

    /// <summary>
    /// Decides whether a candidate is the client the caller asked for. The image path is the
    /// authority when it is readable: a Unix kernel truncates the reported process name, so the
    /// path is the only place the full name survives for a client such as GeneralsOnlineZH_60.
    /// The reported name is the fallback for a process whose image path cannot be read.
    /// </summary>
    /// <param name="candidate">The candidate to test.</param>
    /// <param name="processName">The expected process name, without extension.</param>
    /// <returns><see langword="true"/> when the candidate carries the expected name.</returns>
    private static bool NameMatches(GameProcessCandidate candidate, string processName)
    {
        var imageName = candidate.ExecutablePath is null ? null : Path.GetFileName(candidate.ExecutablePath);

        if (!string.IsNullOrEmpty(imageName))
        {
            // A Unix binary carries no extension and may legitimately contain dots, so both
            // spellings of the file name have to be offered before the candidate is rejected.
            return imageName.Equals(processName, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileNameWithoutExtension(imageName).Equals(processName, StringComparison.OrdinalIgnoreCase);
        }

        return candidate.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase)
            || candidate.ProcessName.Equals(GetDiscoveryName(processName), StringComparison.OrdinalIgnoreCase);
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

    private static string Normalize(string path)
    {
        // MainModule.FileName is always absolute and fully resolved, while the configured working
        // directory is neither guaranteed. Canonicalize first so a relative spelling or a "."
        // segment does not read as a different directory and abandon an adoptable process.
        try
        {
            path = Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            // A malformed path compares on its original spelling rather than aborting the scan.
        }
        catch (NotSupportedException)
        {
            // A malformed path compares on its original spelling rather than aborting the scan.
        }
        catch (PathTooLongException)
        {
            // A malformed path compares on its original spelling rather than aborting the scan.
        }

        return path
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/')
            .TrimEnd('/');
    }
}
