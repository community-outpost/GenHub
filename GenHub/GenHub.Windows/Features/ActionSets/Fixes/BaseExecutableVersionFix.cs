namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

/// <summary>
/// Abstract base class for executable version verification fixes.
/// </summary>
/// <param name="logger">The logger instance.</param>
public abstract class BaseExecutableVersionFix(ILogger logger) : BaseActionSet(logger)
{
    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        try
        {
            if (!HasGame(installation))
            {
                return Task.FromResult(false);
            }

            var exePath = FindExecutable(GetGamePath(installation));
            if (exePath == null)
            {
                return Task.FromResult(false);
            }

            var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
            var version = versionInfo.FileVersion;

            if (version != null && VersionPrefixes.Any(p => version.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error checking {Game} executable version", GameDisplayName);
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Gets the display name of the target game.
    /// </summary>
    protected abstract string GameDisplayName { get; }

    /// <summary>
    /// Gets the expected version string display.
    /// </summary>
    protected abstract string TargetVersionDisplay { get; }

    /// <summary>
    /// Gets the list of valid version prefixes for this game executable.
    /// </summary>
    protected abstract IReadOnlyList<string> VersionPrefixes { get; }

    /// <summary>
    /// Gets candidate executable file names to locate in the game directory.
    /// </summary>
    protected abstract IReadOnlyList<string> CandidateExecutableNames { get; }

    /// <summary>
    /// Checks whether the game installation contains the targeted game.
    /// </summary>
    /// <param name="installation">The targeted game installation.</param>
    /// <returns><c>true</c> if present; otherwise, <c>false</c>.</returns>
    protected abstract bool HasGame(GameInstallation installation);

    /// <summary>
    /// Gets the path to the game directory.
    /// </summary>
    /// <param name="installation">The targeted game installation.</param>
    /// <returns>The game directory path.</returns>
    protected abstract string? GetGamePath(GameInstallation installation);

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        var details = new List<string>();

        try
        {
            if (!HasGame(installation))
            {
                details.Add($"✗ {GameDisplayName} is not installed");
                return Task.FromResult(new ActionSetResult(false, $"{GameDisplayName} is not installed in this installation.", details));
            }

            details.Add($"{GameDisplayName} Executable Fix - Informational");
            details.Add(string.Empty);
            details.Add($"This fix ensures the {GameDisplayName} {TargetVersionDisplay} patch is applied.");
            details.Add(string.Empty);

            var gamePath = GetGamePath(installation);
            var exePath = FindExecutable(gamePath);

            if (exePath != null)
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
                var version = versionInfo.FileVersion;

                details.Add($"Current executable: {Path.GetFileName(exePath)}");
                details.Add($"Current version: {version ?? "unknown"}");

                if (version != null && VersionPrefixes.Any(p => version.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                {
                    details.Add($"✓ {GameDisplayName} {TargetVersionDisplay} patch is already applied");
                    return Task.FromResult(new ActionSetResult(true, null, details));
                }

                details.Add($"⚠ {GameDisplayName} {TargetVersionDisplay} patch needs to be applied");
                details.Add("  Please use the appropriate patch in GenHub to update your game client.");
                return Task.FromResult(new ActionSetResult(false, $"{GameDisplayName} executable is not version {TargetVersionDisplay}.", details));
            }

            details.Add($"⚠ {GameDisplayName} executable not found in: {gamePath}");
            return Task.FromResult(new ActionSetResult(false, $"{GameDisplayName} executable not found in {gamePath}", details));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error checking {Game} executable version", GameDisplayName);
            details.Add($"✗ Error: {ex.Message}");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        Logger.LogWarning("Undoing {Game} Executable Fix is not supported.", GameDisplayName);
        return Task.FromResult(new ActionSetResult(true));
    }

    /// <summary>
    /// Finds the first matching executable path in the specified game directory.
    /// </summary>
    /// <param name="gamePath">The game directory path.</param>
    /// <returns>The path to the located executable, or <c>null</c> if not found.</returns>
    protected string? FindExecutable(string? gamePath)
    {
        if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
        {
            return null;
        }

        return CandidateExecutableNames
            .Select(exe => Path.Combine(gamePath, exe))
            .FirstOrDefault(File.Exists);
    }
}
