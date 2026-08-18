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

    /// <summary>
    /// Decides whether a candidate runs from the expected directory. The image path is fully
    /// symlink-resolved by the operating system while a configured working directory is not, so a
    /// plain string comparison misses a workspace reached through a link — the /var against
    /// /private/var spelling on macOS being the everyday case. Canonicalizing through the
    /// filesystem also settles case: the on-disk spelling of every component is recovered under the
    /// platform's own matching rules, so <see cref="PathHelper.PathComparison"/> accepts a
    /// differently cased path on a case-insensitive volume and still keeps two directories that
    /// differ only in case apart on a case-sensitive one.
    /// </summary>
    /// <param name="candidate">The candidate to test.</param>
    /// <param name="workingDirectory">The directory the game must run from.</param>
    /// <returns><see langword="true"/> when the candidate runs from that directory.</returns>
    private static bool ResidesIn(GameProcessCandidate candidate, string workingDirectory)
    {
        if (candidate.ExecutablePath is null)
        {
            return false;
        }

        var directory = Path.GetDirectoryName(candidate.ExecutablePath);
        if (string.IsNullOrEmpty(directory))
        {
            return false;
        }

        var candidateDirectory = Normalize(directory);
        var expectedDirectory = Normalize(workingDirectory);

        if (candidateDirectory.Equals(expectedDirectory, PathHelper.PathComparison))
        {
            return true;
        }

        return Normalize(Canonicalize(candidateDirectory))
            .Equals(Normalize(Canonicalize(expectedDirectory)), PathHelper.PathComparison);
    }

    /// <summary>
    /// Rewrites a path so every component carries its real on-disk name and no component is a
    /// symbolic link. A component that cannot be inspected is left exactly as it was spelled, so a
    /// missing or malformed path degrades to the plain comparison instead of aborting the scan.
    /// </summary>
    /// <param name="path">The path to canonicalize.</param>
    /// <returns>The canonicalized path.</returns>
    private static string Canonicalize(string path) => Canonicalize(path, depth: 0);

    private static string Canonicalize(string path, int depth)
    {
        var full = TryGetFullPath(path);
        if (full is null)
        {
            return path;
        }

        var resolved = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(resolved))
        {
            return path;
        }

        var segments = full[resolved.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            resolved = ResolveSegment(resolved, segment, depth);
        }

        return resolved;
    }

    private static string ResolveSegment(string parent, string segment, int depth)
    {
        var combined = Path.Combine(parent, OnDiskName(parent, segment));
        if (depth >= IoConstants.MaxSymbolicLinkResolutionDepth)
        {
            return combined;
        }

        var target = TryResolveLinkTarget(combined);

        // A link target is spelled by whoever created the link, so it may be reached through
        // links of its own and has to go back through the same walk.
        return target is null ? combined : Canonicalize(target, depth + 1);
    }

    private static string? TryResolveLinkTarget(string path)
    {
        try
        {
            return Directory.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName;
        }
        catch (IOException)
        {
            // An unreadable or missing component leaves the caller's spelling in place.
        }
        catch (UnauthorizedAccessException)
        {
            // An unreadable or missing component leaves the caller's spelling in place.
        }
        catch (ArgumentException)
        {
            // A malformed component leaves the caller's spelling in place.
        }

        return null;
    }

    /// <summary>
    /// Recovers the spelling a directory entry actually has on disk. Enumeration matches under the
    /// platform's own case rules, so this changes nothing on a case-sensitive volume and folds case
    /// on a volume that does.
    /// </summary>
    /// <param name="parent">The directory to look in.</param>
    /// <param name="segment">The name as it was spelled by the caller.</param>
    /// <returns>The on-disk name, or <paramref name="segment"/> when it cannot be established.</returns>
    private static string OnDiskName(string parent, string segment)
    {
        try
        {
            var entries = Directory.GetFileSystemEntries(parent, segment);
            if (entries.Length == 1)
            {
                var onDisk = Path.GetFileName(entries[0]);

                // A name is also a search pattern, so an entry matched through a wildcard has to be
                // rejected rather than substituted for a name that was never on disk.
                if (onDisk.Equals(segment, StringComparison.OrdinalIgnoreCase))
                {
                    return onDisk;
                }
            }
        }
        catch (IOException)
        {
            // An unreadable directory leaves the caller's spelling in place.
        }
        catch (UnauthorizedAccessException)
        {
            // An unreadable directory leaves the caller's spelling in place.
        }
        catch (ArgumentException)
        {
            // A malformed name leaves the caller's spelling in place.
        }

        return segment;
    }

    private static string Normalize(string path)
    {
        // MainModule.FileName is always absolute and fully resolved, while the configured working
        // directory is neither guaranteed. Canonicalize first so a relative spelling or a "."
        // segment does not read as a different directory and abandon an adoptable process.
        return (TryGetFullPath(path) ?? path)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/')
            .TrimEnd('/');
    }

    private static string? TryGetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
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

        return null;
    }
}
