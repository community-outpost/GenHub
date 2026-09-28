using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;

namespace GenHub.Core.Helpers;

/// <summary>
/// Shared desktop shortcut file operations for the platform shortcut services.
/// </summary>
public static class ShortcutFileHelper
{
    /// <summary>
    /// Deletes a desktop shortcut file, logging the outcome.
    /// </summary>
    /// <param name="shortcutPath">The shortcut file path.</param>
    /// <param name="profileName">The profile name used in log messages.</param>
    /// <param name="logger">The calling service's logger.</param>
    /// <returns>True when a shortcut was removed, false when none existed.</returns>
    public static Task<OperationResult<bool>> RemoveShortcutFileAsync(string shortcutPath, string profileName, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(shortcutPath);

        try
        {
            if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
                logger.LogInformation(
                    "Removed desktop shortcut for profile {ProfileName} at {ShortcutPath}",
                    profileName,
                    shortcutPath);

                return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
            }

            logger.LogWarning("Shortcut not found at {ShortcutPath}", shortcutPath);
            return Task.FromResult(OperationResult<bool>.CreateSuccess(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            logger.LogError(ex, "Failed to remove desktop shortcut for profile {ProfileName}", profileName);
            return Task.FromResult(OperationResult<bool>.CreateFailure($"Failed to remove shortcut: {ex.Message}"));
        }
    }
}
