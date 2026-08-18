using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.MacOS.Features.Shortcuts;

/// <summary>
/// Provides an explicit placeholder for macOS shortcut support.
/// </summary>
public sealed class MacOSShortcutService(ILogger<MacOSShortcutService> logger) : IShortcutService
{
    private const string ShortcutExtension = ".command";

    /// <inheritdoc />
    public Task<OperationResult<string>> CreateDesktopShortcutAsync(
        GameProfile profile,
        string? shortcutName = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        logger.LogWarning(
            "Desktop shortcut creation is not implemented on macOS for profile {ProfileName}",
            profile.Name);

        return Task.FromResult(
            OperationResult<string>.CreateFailure(
                "Desktop shortcut creation is not implemented on macOS yet."));
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> RemoveDesktopShortcutAsync(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        try
        {
            var shortcutPath = GetShortcutPath(profile);
            if (!File.Exists(shortcutPath))
            {
                return Task.FromResult(OperationResult<bool>.CreateSuccess(false));
            }

            File.Delete(shortcutPath);
            return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to remove macOS shortcut for profile {ProfileName}", profile.Name);
            return Task.FromResult(
                OperationResult<bool>.CreateFailure($"Failed to remove shortcut: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public Task<bool> ShortcutExistsAsync(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Task.FromResult(File.Exists(GetShortcutPath(profile)));
    }

    /// <inheritdoc />
    public string GetShortcutPath(GameProfile profile, string? shortcutName = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (string.IsNullOrWhiteSpace(desktopPath))
        {
            desktopPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Desktop");
        }

        var name = SanitizeFileName(shortcutName ?? profile.Name);
        return Path.Combine(desktopPath, $"{AppConstants.AppName}-{name}{ShortcutExtension}");
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> CreateShortcutAsync(
        string shortcutPath,
        string targetPath,
        string? arguments = null,
        string? workingDirectory = null,
        string? description = null,
        string? iconPath = null)
    {
        logger.LogWarning(
            "Shortcut creation is not implemented on macOS yet for target {TargetPath}",
            targetPath);

        return Task.FromResult(
            OperationResult<bool>.CreateFailure(
                "Shortcut creation is not implemented on macOS yet."));
    }

    private static string SanitizeFileName(string fileName)
    {
        var sanitized = new StringBuilder(fileName);
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            sanitized.Replace(invalidCharacter, '_');
        }

        return sanitized.ToString().Trim();
    }
}
