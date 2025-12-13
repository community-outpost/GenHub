using System;
using System.IO;
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

            var workingDirectory = Path.GetDirectoryName(executablePath);
            var arguments = $"--launch-profile \"{profile.Id}\"";
            var description = $"Launch {profile.Name} with GenHub";

            // Use the profile's icon if available, otherwise use the application icon
            var iconPath = !string.IsNullOrEmpty(profile.IconPath) && File.Exists(profile.IconPath)
                ? profile.IconPath
                : executablePath;

            CreateShortcut(shortcutPath, executablePath, arguments, workingDirectory, description, iconPath);

            logger.LogInformation(
                "Created desktop shortcut for profile {ProfileName} at {ShortcutPath}",
                profile.Name,
                shortcutPath);

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

    /// <summary>
    /// Creates a Windows shortcut (.lnk file) using COM interop.
    /// </summary>
    /// <param name="shortcutPath">The path where the shortcut will be created.</param>
    /// <param name="targetPath">The path to the target executable.</param>
    /// <param name="arguments">Command line arguments for the target.</param>
    /// <param name="workingDirectory">The working directory for the target.</param>
    /// <param name="description">The description/tooltip for the shortcut.</param>
    /// <param name="iconPath">The path to the icon file.</param>
    private static void CreateShortcut(
        string shortcutPath,
        string targetPath,
        string? arguments,
        string? workingDirectory,
        string? description,
        string? iconPath)
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
        /// <param name="pfd">File data pointer.</param>
        /// <param name="fFlags">Flags.</param>
        void GetPath(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
            int cchMaxPath,
            out IntPtr pfd,
            int fFlags);

        /// <summary>Gets the ID list for the target.</summary>
        /// <param name="ppidl">Pointer to receive the ID list.</param>
        void GetIDList(out IntPtr ppidl);

        /// <summary>Sets the ID list for the target.</summary>
        /// <param name="pidl">The ID list.</param>
        void SetIDList(IntPtr pidl);

        /// <summary>Gets the description of the shortcut.</summary>
        /// <param name="pszName">Buffer to receive the description.</param>
        /// <param name="cchMaxName">Maximum description length.</param>
        void GetDescription(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName,
            int cchMaxName);

        /// <summary>Sets the description of the shortcut.</summary>
        /// <param name="pszName">The description.</param>
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        /// <summary>Gets the working directory for the shortcut.</summary>
        /// <param name="pszDir">Buffer to receive the directory.</param>
        /// <param name="cchMaxPath">Maximum path length.</param>
        void GetWorkingDirectory(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir,
            int cchMaxPath);

        /// <summary>Sets the working directory for the shortcut.</summary>
        /// <param name="pszDir">The working directory.</param>
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        /// <summary>Gets the command line arguments.</summary>
        /// <param name="pszArgs">Buffer to receive the arguments.</param>
        /// <param name="cchMaxPath">Maximum arguments length.</param>
        void GetArguments(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs,
            int cchMaxPath);

        /// <summary>Sets the command line arguments.</summary>
        /// <param name="pszArgs">The arguments.</param>
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

        /// <summary>Gets the hotkey for the shortcut.</summary>
        /// <param name="pwHotkey">The hotkey value.</param>
        void GetHotkey(out short pwHotkey);

        /// <summary>Sets the hotkey for the shortcut.</summary>
        /// <param name="wHotkey">The hotkey value.</param>
        void SetHotkey(short wHotkey);

        /// <summary>Gets the show command for the shortcut.</summary>
        /// <param name="piShowCmd">The show command value.</param>
        void GetShowCmd(out int piShowCmd);

        /// <summary>Sets the show command for the shortcut.</summary>
        /// <param name="iShowCmd">The show command value.</param>
        void SetShowCmd(int iShowCmd);

        /// <summary>Gets the icon location for the shortcut.</summary>
        /// <param name="pszIconPath">Buffer to receive the icon path.</param>
        /// <param name="cchIconPath">Maximum path length.</param>
        /// <param name="piIcon">The icon index.</param>
        void GetIconLocation(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
            int cchIconPath,
            out int piIcon);

        /// <summary>Sets the icon location for the shortcut.</summary>
        /// <param name="pszIconPath">The icon path.</param>
        /// <param name="iIcon">The icon index.</param>
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

        /// <summary>Sets the relative path to the target.</summary>
        /// <param name="pszPathRel">The relative path.</param>
        /// <param name="dwReserved">Reserved.</param>
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);

        /// <summary>Resolves the shortcut link.</summary>
        /// <param name="hwnd">The window handle.</param>
        /// <param name="fFlags">Flags.</param>
        void Resolve(IntPtr hwnd, int fFlags);

        /// <summary>Sets the path to the target file.</summary>
        /// <param name="pszFile">The target path.</param>
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
