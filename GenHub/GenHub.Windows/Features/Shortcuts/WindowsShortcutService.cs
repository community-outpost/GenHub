using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Windows.Features.Shortcuts;

/// <summary>
/// Windows implementation of <see cref="IShortcutService"/> that creates .lnk shortcuts.
/// </summary>
public class WindowsShortcutService(ILogger<WindowsShortcutService> logger) : IShortcutService
{
    /// <summary>
    /// COM class for creating shell links.
    /// </summary>
    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    /// <summary>
    /// COM interface for shell link operations.
    /// </summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLink
    {
        /// <summary>Gets the path of the target file.</summary>
        /// <param name="pszFile">Buffer to receive the path.</param>
        /// <param name="cchMaxPath">Maximum path length.</param>
        /// <param name="pfd">Find data structure.</param>
        /// <param name="fFlags">Flags.</param>
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);

        /// <summary>Gets the list of shell link item identifiers.</summary>
        /// <param name="ppidl">Pointer to receive item ID list.</param>
        void GetIDList(out IntPtr ppidl);

        /// <summary>Sets the list of shell link item identifiers.</summary>
        /// <param name="pidl">Pointer to item ID list.</param>
        void SetIDList(IntPtr pidl);

        /// <summary>Gets the shell link description.</summary>
        /// <param name="pszName">Buffer to receive description.</param>
        /// <param name="cchMaxName">Maximum description length.</param>
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);

        /// <summary>Sets the shell link description.</summary>
        /// <param name="pszName">Description string.</param>
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        /// <summary>Gets the shell link working directory.</summary>
        /// <param name="pszDir">Buffer to receive directory path.</param>
        /// <param name="cchMaxPath">Maximum path length.</param>
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);

        /// <summary>Sets the shell link working directory.</summary>
        /// <param name="pszDir">Working directory path.</param>
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        /// <summary>Gets the shell link arguments.</summary>
        /// <param name="pszArgs">Buffer to receive arguments.</param>
        /// <param name="cchMaxPath">Maximum arguments length.</param>
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);

        /// <summary>Sets the shell link arguments.</summary>
        /// <param name="pszArgs">Arguments string.</param>
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

        /// <summary>Gets the shell link hot key.</summary>
        /// <param name="pwHotkey">Pointer to receive hot key.</param>
        void GetHotkey(out short pwHotkey);

        /// <summary>Sets the shell link hot key.</summary>
        /// <param name="wHotkey">Hot key value.</param>
        void SetHotkey(short wHotkey);

        /// <summary>Gets the shell link show command.</summary>
        /// <param name="piShowCmd">Pointer to receive show command.</param>
        void GetShowCmd(out int piShowCmd);

        /// <summary>Sets the shell link show command.</summary>
        /// <param name="iShowCmd">Show command value.</param>
        void SetShowCmd(int iShowCmd);

        /// <summary>Gets the shell link icon location.</summary>
        /// <param name="pszIconPath">Buffer to receive icon path.</param>
        /// <param name="cchIconPath">Maximum icon path length.</param>
        /// <param name="piIcon">Pointer to receive icon index.</param>
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);

        /// <summary>Sets the shell link icon location.</summary>
        /// <param name="pszIconPath">Icon path string.</param>
        /// <param name="iIcon">Icon index.</param>
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

        /// <summary>Sets the shell link relative path.</summary>
        /// <param name="pszPathRel">Relative path string.</param>
        /// <param name="dwReserved">Reserved parameter.</param>
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);

        /// <summary>Resolves a shell link.</summary>
        /// <param name="hwnd">Window handle.</param>
        /// <param name="fFlags">Flags.</param>
        void Resolve(IntPtr hwnd, int fFlags);

        /// <summary>Sets the shell link path.</summary>
        /// <param name="pszFile">Path string.</param>
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    /// <summary>
    /// Creates a shortcut using Windows COM interfaces.
    /// </summary>
    private static void CreateShortcut(
        string shortcutPath,
        string targetPath,
        string? arguments = null,
        string? workingDirectory = null,
        string? description = null,
        string? iconPath = null)
    {
        IShellLink? link = null;
        IPersistFile? file = null;

        try
        {
            link = (IShellLink)new ShellLink();
            link.SetPath(targetPath);

            if (!string.IsNullOrEmpty(arguments))
            {
                link.SetArguments(arguments);
            }

            if (!string.IsNullOrEmpty(workingDirectory))
            {
                link.SetWorkingDirectory(workingDirectory);
            }

            if (!string.IsNullOrEmpty(description))
            {
                link.SetDescription(description);
            }

            if (!string.IsNullOrEmpty(iconPath))
            {
                link.SetIconLocation(iconPath, 0);
            }

            file = (IPersistFile)link;
            file.Save(shortcutPath, false);
        }
        finally
        {
            if (file != null)
            {
                Marshal.ReleaseComObject(file);
            }

            if (link != null)
            {
                Marshal.ReleaseComObject(link);
            }
        }
    }

    /// <summary>
    /// Attempts to read the existing arguments of a shortcut.
    /// </summary>
    private static string? TryReadShortcutArguments(string shortcutPath)
    {
        IShellLink? link = null;
        IPersistFile? file = null;
        try
        {
            link = (IShellLink)new ShellLink();
            file = (IPersistFile)link;
            file.Load(shortcutPath, 0);
            var sb = new StringBuilder(1024);
            link.GetArguments(sb, sb.Capacity);
            var args = sb.ToString();
            return string.IsNullOrWhiteSpace(args) ? null : args;
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            if (file != null)
            {
                Marshal.ReleaseComObject(file);
            }

            if (link != null)
            {
                Marshal.ReleaseComObject(link);
            }
        }
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
                var parentDir = Path.GetDirectoryName(dir);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    var exeName = Path.GetFileName(executablePath);
                    var parentExe = Path.Combine(parentDir, exeName);
                    if (File.Exists(parentExe))
                    {
                        return parentExe;
                    }

                    var appNameExe = Path.Combine(parentDir, $"{AppConstants.AppName}.exe");
                    if (File.Exists(appNameExe))
                    {
                        return appNameExe;
                    }
                }
            }
        }

        return executablePath;
    }

    /// <inheritdoc />
    public Task<OperationResult<string>> CreateDesktopShortcutAsync(GameProfile profile, string? shortcutName = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        try
        {
            var shortcutPath = GetShortcutPath(profile, shortcutName);
            var executablePath = Environment.ProcessPath;

            if (string.IsNullOrEmpty(executablePath))
            {
                logger.LogError("Failed to get current executable path");
                return Task.FromResult(OperationResult<string>.CreateFailure("Failed to get application path"));
            }

            var launcherPath = ResolveLauncherExecutable(executablePath);
            var workingDirectory = Path.GetDirectoryName(launcherPath) ?? string.Empty;
            var arguments = $"--launch-profile \"{profile.Id}\"";
            var description = $"Launch {profile.Name} with GenHub";

            var iconPath = !string.IsNullOrEmpty(profile.IconPath) && File.Exists(profile.IconPath)
                ? profile.IconPath
                : launcherPath;

            var directory = Path.GetDirectoryName(shortcutPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            CreateShortcut(shortcutPath, launcherPath, arguments, workingDirectory, description, iconPath);

            logger.LogInformation(
                "Created desktop shortcut for profile {ProfileName} at {ShortcutPath} targeting {LauncherPath}",
                profile.Name,
                shortcutPath,
                launcherPath);

            return Task.FromResult(OperationResult<string>.CreateSuccess(shortcutPath));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create desktop shortcut for profile {ProfileName}", profile.Name);
            return Task.FromResult(OperationResult<string>.CreateFailure($"Failed to create shortcut: {ex.Message}"));
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

        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var name = SanitizeFileName(shortcutName ?? profile.Name);
        return Path.Combine(desktopPath, $"{AppConstants.AppName}-{name}.lnk");
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> RepairApplicationShortcutsAsync()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
        {
            return Task.FromResult(OperationResult<bool>.CreateSuccess(false));
        }

        try
        {
            var launcherPath = ResolveLauncherExecutable(executablePath);
            var workingDirectory = Path.GetDirectoryName(launcherPath) ?? string.Empty;
            var locations = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{AppConstants.AppName}.lnk"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), $"{AppConstants.AppName}.lnk"),
            };

            foreach (var shortcutPath in locations.Where(File.Exists))
            {
                var existingArgs = TryReadShortcutArguments(shortcutPath);
                CreateShortcut(shortcutPath, launcherPath, existingArgs, workingDirectory, AppConstants.AppName, launcherPath);
                logger.LogInformation("Repaired application shortcut at {ShortcutPath} -> {LauncherPath}", shortcutPath, launcherPath);
            }

            return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to repair application shortcuts");
            return Task.FromResult(OperationResult<bool>.CreateFailure($"Failed to repair application shortcuts: {ex.Message}"));
        }
    }
}
