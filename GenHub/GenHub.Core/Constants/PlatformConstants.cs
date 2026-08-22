using System;
using System.IO;

namespace GenHub.Core.Constants;

/// <summary>
/// Platform-specific constants.
/// </summary>
public static class PlatformConstants
{
    /// <summary>
    /// Windows Explorer executable name.
    /// </summary>
    public const string WindowsExplorerExecutable = "explorer.exe";

    /// <summary>
    /// Windows Explorer select argument.
    /// </summary>
    public const string WindowsExplorerSelectArgument = "/select,\"{0}\"";

    /// <summary>
    /// Gets the absolute path to the Windows Explorer executable.
    /// </summary>
    public static string WindowsExplorerPath
    {
        get
        {
            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return string.IsNullOrEmpty(windowsDir)
                ? WindowsExplorerExecutable
                : Path.Combine(windowsDir, WindowsExplorerExecutable);
        }
    }
}