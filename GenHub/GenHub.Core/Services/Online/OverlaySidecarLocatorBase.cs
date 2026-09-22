using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Online;
using System;
using System.IO;
using System.Text;

namespace GenHub.Core.Services.Online;

/// <summary>
/// Base sidecar locator honoring the binary override and platform candidate paths.
/// </summary>
public abstract class OverlaySidecarLocatorBase : IOverlaySidecarLocator
{
    /// <summary>
    /// Gets the base special folder for the platform candidate path.
    /// </summary>
    protected abstract Environment.SpecialFolder BaseFolder { get; }

    /// <summary>
    /// Gets the path segments below <see cref="BaseFolder"/> locating the platform candidate binary.
    /// </summary>
    protected abstract string[] CandidateSegments { get; }

    /// <inheritdoc/>
    public string? LocateBinary()
    {
        var overridePath = Environment.GetEnvironmentVariable(OnlineConstants.OverlayBinaryEnvVar);
        if (!string.IsNullOrWhiteSpace(overridePath) && IsValidCandidate(overridePath))
        {
            return overridePath;
        }

        var appBase = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(appBase) && CandidateSegments.Length > 0)
        {
            var binaryName = CandidateSegments[^1];
            var localCandidate = Path.Combine(appBase, binaryName);
            if (IsValidCandidate(localCandidate))
            {
                return localCandidate;
            }

            var subDirCandidate = Path.Combine(appBase, OnlineConstants.OverlaySubDir, binaryName);
            if (IsValidCandidate(subDirCandidate))
            {
                return subDirCandidate;
            }
        }

        var basePath = Environment.GetFolderPath(BaseFolder);
        var candidate = Path.Combine([basePath, .. CandidateSegments]);
        return IsValidCandidate(candidate) ? candidate : null;
    }

    /// <inheritdoc/>
    public string BuildArguments(string configPath)
    {
        return $"--config \"{EscapeArgument(configPath)}\"";
    }

    /// <summary>
    /// Determines whether the file has any executable bit set (Unix only).
    /// </summary>
    /// <param name="path">The candidate path.</param>
    /// <returns>True when at least one execute bit is set.</returns>
    protected static bool HasExecutePermission(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return false;
        }

        try
        {
            var mode = File.GetUnixFileMode(path);
            return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Escapes an argument for safe quoting in a process start command line.
    /// </summary>
    /// <param name="value">The raw argument.</param>
    /// <returns>The escaped argument.</returns>
    protected static string EscapeArgument(string value)
    {
        return value.Replace("\"", "\\\"");
    }

    private static bool IsValidCandidate(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        return HasExecutePermission(path);
    }
}
