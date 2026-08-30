using System;
using System.IO;
using System.Runtime.Versioning;
using System.Text;
using GenHub.Core.Constants;
using Microsoft.Extensions.Logging;

namespace GenHub.Linux.Features.Shortcuts;

/// <summary>
/// Registers the <c>genhub://</c> URI scheme handler on Linux desktop environments.
/// </summary>
[SupportedOSPlatform("linux")]
public static class LinuxUriSchemeRegistrar
{
    private const string SchemeName = CommandLineConstants.SchemeName;

    /// <summary>
    /// Registers the <c>genhub://</c> scheme for the current Linux user desktop.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public static void Register(ILogger? logger = null)
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
        {
            logger?.LogWarning("Could not register genhub:// scheme on Linux: executable path unavailable.");
            return;
        }

        try
        {
            var appsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "share",
                "applications");

            Directory.CreateDirectory(appsDir);
            var desktopFilePath = Path.Combine(appsDir, "genhub.desktop");

            var content = new StringBuilder();
            content.AppendLine("[Desktop Entry]");
            content.AppendLine("Version=1.0");
            content.AppendLine("Type=Application");
            content.AppendLine("Name=GenHub");
            content.AppendLine("Comment=Command & Conquer Generals and Zero Hour Hub");
            content.AppendLine($"Exec=\"{executablePath}\" %u");
            content.AppendLine("Icon=genhub");
            content.AppendLine("Terminal=false");
            content.AppendLine("Categories=Game;");
            content.AppendLine($"MimeType=x-scheme-handler/{SchemeName};");

            File.WriteAllText(desktopFilePath, content.ToString(), Encoding.UTF8);
            logger?.LogInformation("Registered Linux genhub:// URI scheme handler at {DesktopFilePath}", desktopFilePath);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to register Linux genhub:// URI scheme handler.");
        }
    }
}
