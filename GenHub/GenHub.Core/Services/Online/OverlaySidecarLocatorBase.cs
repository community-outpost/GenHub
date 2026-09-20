using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Online;
using System;
using System.IO;
using System.Text;

namespace GenHub.Core.Services.Online;

/// <summary>
/// Base sidecar locator honoring the binary override and a single platform candidate path.
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
    /// Determines whether the candidate path is a usable sidecar binary.
    /// Unix platforms additionally require the executable bit.
    /// </summary>
    /// <param name="path">The candidate path.</param>
    /// <returns>True when the binary can be launched.</returns>
    protected virtual bool IsValidCandidate(string path)
    {
        return File.Exists(path);
    }

    private static string EscapeArgument(string value)
    {
        // Quote-aware escaping for argv parsing: double backslashes ahead of
        // a quote or the closing quote, and escape embedded quotes, so paths
        // with quotes or trailing backslashes survive intact.
        var builder = new StringBuilder();
        var backslashes = 0;
        foreach (var c in value)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                builder.Append('\\', (backslashes * 2) + 1);
                builder.Append('"');
            }
            else
            {
                builder.Append('\\', backslashes);
                builder.Append(c);
            }

            backslashes = 0;
        }

        builder.Append('\\', backslashes * 2);
        return builder.ToString();
    }
}
