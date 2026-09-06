using System;
using System.IO;
using System.Linq;
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
/// Linux implementation of <see cref="IShortcutService"/> that creates .desktop files.\n/// </summary>
[SupportedOSPlatform("linux")]
public class LinuxShortcutService(ILogger<LinuxShortcutService> logger) : IShortcutService
{
    private const string DesktopEntryVersion = "1.0";
    private const string DesktopEntryType = "Application";
    private const string DesktopFileExtension = ".desktop";

    private static readonly UTF8Encoding Utf8NoBom = new(false);

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

            var directory = Path.GetDirectoryName(shortcutPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(shortcutPath, desktopEntry, Utf8NoBom);

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

    /// <inheritdoc />
    public Task<OperationResult<bool>> RepairApplicationShortcutsAsync()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath) || !File.Exists(processPath))
        {
            return Task.FromResult(OperationResult<bool>.CreateSuccess(false));
        }

        try
        {
            var executablePath = ResolveLauncherExecutable(processPath);
            var workingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty;
            var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrWhiteSpace(dataHome))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                dataHome = Path.Combine(home, ".local", "share");
            }

            var appDir = Path.Combine(dataHome, "applications");
            var locations = new[]
            {
                Path.Combine(appDir, $"{AppConstants.AppName}{DesktopFileExtension}"),
                Path.Combine(appDir, $"community-outpost.{AppConstants.AppName}{DesktopFileExtension}"),
                Path.Combine(GetDesktopPath(), $"{AppConstants.AppName}{DesktopFileExtension}"),
            };

            foreach (var shortcutPath in locations.Where(File.Exists))
            {
                TryRepairDesktopEntry(shortcutPath, executablePath, workingDirectory);
            }

            return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to repair application shortcuts");
            return Task.FromResult(OperationResult<bool>.CreateFailure($"Failed to repair application shortcuts: {ex.Message}"));
        }
    }

    /// <summary>
    /// Builds a .desktop file content following the freedesktop.org specification.
    /// </summary>
    private static string BuildDesktopEntry(
        string name,
        string comment,
        string executablePath,
        string arguments,
        string workingDirectory,
        string iconPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Desktop Entry]");
        sb.AppendLine($"Version={DesktopEntryVersion}");
        sb.AppendLine($"Type={DesktopEntryType}");
        sb.AppendLine($"Name={EscapeDesktopValue(name)}");

        if (!string.IsNullOrEmpty(comment))
        {
            sb.AppendLine($"Comment={EscapeDesktopValue(comment)}");
        }

        var execValue = string.IsNullOrEmpty(arguments)
            ? EscapeExecValue(executablePath)
            : $"{EscapeExecValue(executablePath)} {arguments}";
        sb.AppendLine($"Exec={execValue}");

        if (!string.IsNullOrEmpty(iconPath))
        {
            sb.AppendLine($"Icon={EscapeDesktopValue(iconPath)}");
        }

        if (!string.IsNullOrEmpty(workingDirectory))
        {
            sb.AppendLine($"Path={EscapeDesktopValue(workingDirectory)}");
        }

        sb.AppendLine("Terminal=false");
        sb.AppendLine("Categories=Game;");
        sb.AppendLine("StartupNotify=true");

        return sb.ToString();
    }

    /// <summary>
    /// Escapes special characters in desktop entry values according to the freedesktop.org specification.
    /// </summary>
    private static string EscapeDesktopValue(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t")
            .Replace(";", "\\;");
    }

    /// <summary>
    /// Escapes special characters in Exec field values.
    /// </summary>
    private static string EscapeExecValue(string value)
    {
        var escaped = value.Replace("%", "%%");
        if (escaped.Contains(' ', StringComparison.Ordinal) || escaped.Contains('"', StringComparison.Ordinal))
        {
            escaped = escaped.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return $"\"{escaped}\"";
        }

        return escaped;
    }

    /// <summary>
    /// Updates the executable path in an Exec desktop entry line while preserving existing arguments.
    /// </summary>
    private static string ReplaceExecutableInExecLine(string execLine, string newExecutablePath)
    {
        var value = execLine[5..].Trim();
        var args = string.Empty;
        if (value.StartsWith('\"'))
        {
            var endQuote = value.IndexOf('\"', 1);
            if (endQuote >= 0 && endQuote + 1 < value.Length)
            {
                args = value[(endQuote + 1)..].Trim();
            }
        }
        else
        {
            var spaceIndex = value.IndexOf(' ');
            if (spaceIndex >= 0)
            {
                args = value[spaceIndex..].Trim();
            }
        }

        var escapedExe = EscapeExecValue(newExecutablePath);
        return string.IsNullOrEmpty(args) ? $"Exec={escapedExe}" : $"Exec={escapedExe} {args}";
    }

    /// <summary>
    /// Sanitizes a file name by removing or replacing invalid characters.
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new StringBuilder(fileName);

        foreach (var c in invalidChars)
        {
            sanitized.Replace(c, '_');
        }

        sanitized.Replace(' ', '_');

        return sanitized.ToString().Trim();
    }

    /// <summary>
    /// Resolves the stable Velopack root launcher executable if running from a versioned directory.
    /// </summary>
    /// <param name="executablePath">The running process executable path.</param>
    /// <returns>The root launcher path if available; otherwise the original executable path.</returns>
    private static string ResolveLauncherExecutable(string executablePath)
    {
        var dir = Path.GetDirectoryName(executablePath);
        if (!string.IsNullOrEmpty(dir))
        {
            var dirName = Path.GetFileName(dir);
            if (string.Equals(dirName, StorageMigrationConstants.CurrentDirectoryName, StringComparison.OrdinalIgnoreCase) ||
                dirName.StartsWith(StorageMigrationConstants.AppDirectoryPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var parentDir = Directory.GetParent(dir)?.FullName;
                if (!string.IsNullOrEmpty(parentDir))
                {
                    var parentExe = Path.Combine(parentDir, AppConstants.AppName);
                    if (File.Exists(parentExe))
                    {
                        return parentExe;
                    }
                }
            }
        }

        return executablePath;
    }

    /// <summary>
    /// Gets the user's desktop path, following XDG standards if available.
    /// </summary>
    private static string GetDesktopPath()
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (string.IsNullOrEmpty(desktopPath))
        {
            desktopPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Desktop");
        }

        return desktopPath;
    }

    /// <summary>
    /// Makes a file executable using chmod.
    /// </summary>
    private void MakeExecutable(string filePath)
    {
        try
        {
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

    private void TryRepairDesktopEntry(string shortcutPath, string executablePath, string workingDirectory)
    {
        var lines = File.ReadAllLines(shortcutPath);
        var updated = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("Exec=", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = ReplaceExecutableInExecLine(lines[i], executablePath);
                updated = true;
            }
            else if (lines[i].StartsWith("Path=", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = $"Path={workingDirectory}";
                updated = true;
            }
        }

        if (updated)
        {
            File.WriteAllLines(shortcutPath, lines, Utf8NoBom);
            MakeExecutable(shortcutPath);
            logger.LogInformation("Repaired application desktop entry at {ShortcutPath} -> {ExecutablePath}", shortcutPath, executablePath);
        }
    }
}
