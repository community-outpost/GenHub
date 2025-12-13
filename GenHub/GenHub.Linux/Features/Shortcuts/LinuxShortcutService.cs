using System;
using System.IO;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Linux.Features.Shortcuts;

/// <summary>
/// Linux implementation of <see cref="IShortcutService"/> that creates .desktop files.
/// </summary>
[SupportedOSPlatform("linux")]
public class LinuxShortcutService(ILogger<LinuxShortcutService> logger) : IShortcutService
{
    private const string DesktopEntryVersion = "1.0";
    private const string DesktopEntryType = "Application";
    private const string DesktopFileExtension = ".desktop";

    /// <inheritdoc />
    public async Task<OperationResult<string>> CreateDesktopShortcutAsync(GameProfile profile, string? shortcutName = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        try
        {
            var shortcutPath = GetShortcutPath(profile, shortcutName);
            var executablePath = Environment.ProcessPath;

            if (string.IsNullOrEmpty(executablePath))
            {
                logger.LogError("Failed to get current executable path");
                return OperationResult<string>.CreateFailure("Failed to get application path");
            }

            var workingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty;
            var arguments = $"--launch-profile \"{profile.Id}\"";
            var comment = $"Launch {profile.Name} with GenHub";
            var name = shortcutName ?? profile.Name;

            // Use the profile's icon if available
            var iconPath = !string.IsNullOrEmpty(profile.IconPath) && File.Exists(profile.IconPath)
                ? profile.IconPath
                : string.Empty;

            var desktopEntry = BuildDesktopEntry(
                name,
                comment,
                executablePath,
                arguments,
                workingDirectory,
                iconPath);

            // Ensure the directory exists
            var directory = Path.GetDirectoryName(shortcutPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(shortcutPath, desktopEntry, Encoding.UTF8);

            // Make the .desktop file executable
            MakeExecutable(shortcutPath);

            logger.LogInformation(
                "Created desktop shortcut for profile {ProfileName} at {ShortcutPath}",
                profile.Name,
                shortcutPath);

            return OperationResult<string>.CreateSuccess(shortcutPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create desktop shortcut for profile {ProfileName}", profile.Name);
            return OperationResult<string>.CreateFailure($"Failed to create shortcut: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> RemoveDesktopShortcutAsync(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        try
        {
            var shortcutPath = GetShortcutPath(profile);

            if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
                logger.LogInformation(
                    "Removed desktop shortcut for profile {ProfileName} at {ShortcutPath}",
                    profile.Name,
                    shortcutPath);

                return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
            }

            logger.LogWarning("Shortcut not found at {ShortcutPath}", shortcutPath);
            return Task.FromResult(OperationResult<bool>.CreateSuccess(false));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to remove desktop shortcut for profile {ProfileName}", profile.Name);
            return Task.FromResult(OperationResult<bool>.CreateFailure($"Failed to remove shortcut: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public Task<bool> ShortcutExistsAsync(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var shortcutPath = GetShortcutPath(profile);
        return Task.FromResult(File.Exists(shortcutPath));
    }

    /// <inheritdoc />
    public string GetShortcutPath(GameProfile profile, string? shortcutName = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var desktopPath = GetDesktopPath();
        var name = SanitizeFileName(shortcutName ?? profile.Name);
        return Path.Combine(desktopPath, $"{AppConstants.AppName}-{name}{DesktopFileExtension}");
    }

    /// <summary>
    /// Builds a .desktop file content following the freedesktop.org specification.
    /// </summary>
    /// <param name="name">The display name for the shortcut.</param>
    /// <param name="comment">A description/tooltip for the shortcut.</param>
    /// <param name="executablePath">The path to the executable.</param>
    /// <param name="arguments">Command line arguments.</param>
    /// <param name="workingDirectory">The working directory.</param>
    /// <param name="iconPath">The path to the icon.</param>
    /// <returns>The .desktop file content.</returns>
    private static string BuildDesktopEntry(
        string name,
        string comment,
        string executablePath,
        string arguments,
        string workingDirectory,
        string iconPath)
    {
        var builder = new StringBuilder();

        builder.AppendLine("[Desktop Entry]");
        builder.AppendLine($"Version={DesktopEntryVersion}");
        builder.AppendLine($"Type={DesktopEntryType}");
        builder.AppendLine($"Name={EscapeDesktopValue(name)}");
        builder.AppendLine($"Comment={EscapeDesktopValue(comment)}");
        builder.AppendLine($"Exec={EscapeExecValue(executablePath)} {arguments}");

        if (!string.IsNullOrEmpty(workingDirectory))
        {
            builder.AppendLine($"Path={workingDirectory}");
        }

        if (!string.IsNullOrEmpty(iconPath))
        {
            builder.AppendLine($"Icon={iconPath}");
        }

        builder.AppendLine("Terminal=false");
        builder.AppendLine("Categories=Game;");

        return builder.ToString();
    }

    /// <summary>
    /// Gets the path to the user's desktop directory.
    /// </summary>
    /// <returns>The desktop directory path.</returns>
    private static string GetDesktopPath()
    {
        // Try XDG_DESKTOP_DIR first
        var xdgDesktopDir = Environment.GetEnvironmentVariable("XDG_DESKTOP_DIR");
        if (!string.IsNullOrEmpty(xdgDesktopDir) && Directory.Exists(xdgDesktopDir))
        {
            return xdgDesktopDir;
        }

        // Fall back to ~/Desktop
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var desktopPath = Path.Combine(home, "Desktop");

        // If Desktop doesn't exist, try the XDG user directories config
        if (!Directory.Exists(desktopPath))
        {
            var xdgConfigPath = Path.Combine(home, ".config", "user-dirs.dirs");
            if (File.Exists(xdgConfigPath))
            {
                var lines = File.ReadAllLines(xdgConfigPath);
                foreach (var line in lines)
                {
                    if (line.StartsWith("XDG_DESKTOP_DIR=", StringComparison.Ordinal))
                    {
                        var value = line.Substring("XDG_DESKTOP_DIR=".Length).Trim('"');
                        value = value.Replace("$HOME", home);
                        if (Directory.Exists(value))
                        {
                            return value;
                        }
                    }
                }
            }
        }

        return desktopPath;
    }

    /// <summary>
    /// Escapes special characters in desktop entry values.
    /// </summary>
    /// <param name="value">The value to escape.</param>
    /// <returns>The escaped value.</returns>
    private static string EscapeDesktopValue(string value)
    {
        // Escape backslashes, newlines, tabs, and semicolons
        return value
            .Replace("\\", "\\\\")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t")
            .Replace(";", "\\;");
    }

    /// <summary>
    /// Escapes special characters in Exec field values.
    /// </summary>
    /// <param name="value">The value to escape.</param>
    /// <returns>The escaped value.</returns>
    private static string EscapeExecValue(string value)
    {
        // Quote the executable path if it contains spaces
        if (value.Contains(' ', StringComparison.Ordinal))
        {
            return $"\"{value}\"";
        }

        return value;
    }

    /// <summary>
    /// Sanitizes a file name by removing or replacing invalid characters.
    /// </summary>
    /// <param name="fileName">The file name to sanitize.</param>
    /// <returns>A sanitized file name.</returns>
    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new StringBuilder(fileName);

        foreach (var c in invalidChars)
        {
            sanitized.Replace(c, '_');
        }

        // Also replace spaces with underscores for Linux desktop files
        sanitized.Replace(' ', '_');

        return sanitized.ToString().Trim();
    }

    /// <summary>
    /// Makes a file executable using chmod.
    /// </summary>
    /// <param name="filePath">The path to the file.</param>
    private void MakeExecutable(string filePath)
    {
        try
        {
            // Use File.SetUnixFileMode if available (.NET 7+)
            var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                       UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                       UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(filePath, mode);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to set executable permissions on {FilePath}", filePath);
        }
    }
}
