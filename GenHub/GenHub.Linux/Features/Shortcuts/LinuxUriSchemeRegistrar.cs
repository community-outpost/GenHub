using GenHub.Core.Constants;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;

namespace GenHub.Linux.Features.Shortcuts;

/// <summary>
/// Registers the <c>genhub://</c> custom URL scheme on Linux using the freedesktop desktop-entry specification.
/// </summary>
public static class LinuxUriSchemeRegistrar
{
    private const string SchemeName = CommandLineConstants.SchemeName;
    private const int CommandTimeoutMs = 3000;

    /// <summary>
    /// Registers the <c>genhub://</c> scheme and profile MIME type for the current Linux user desktop.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public static void Register(ILogger? logger = null)
    {
        var appImagePath = Environment.GetEnvironmentVariable(CommandLineConstants.AppImageEnvVar);
        var executablePath = (!string.IsNullOrWhiteSpace(appImagePath) && File.Exists(appImagePath))
            ? appImagePath
            : Environment.ProcessPath;

        if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
        {
            logger?.LogWarning("Could not register genhub:// scheme on Linux: executable path unavailable.");
            return;
        }

        try
        {
            var dataHome = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appsDir = Path.Combine(dataHome, "applications");

            Directory.CreateDirectory(appsDir);
            var desktopFilePath = Path.Combine(appsDir, "genhub.desktop");

            var escapedExecPath = executablePath.Replace("\\", "\\\\").Replace("\"", "\\\"");

            var content = new StringBuilder();
            content.AppendLine("[Desktop Entry]");
            content.AppendLine("Type=Application");
            content.AppendLine("Name=GenHub");
            content.AppendLine($"Exec=\"{escapedExecPath}\" %u");
            content.AppendLine("Terminal=false");
            content.AppendLine("Categories=Game;");
            content.AppendLine($"MimeType=x-scheme-handler/{SchemeName};application/x-genhub-profile;");
            content.AppendLine("NoDisplay=true");

            var desiredContent = content.ToString();
            if (File.Exists(desktopFilePath) && string.Equals(File.ReadAllText(desktopFilePath), desiredContent, StringComparison.Ordinal))
            {
                logger?.LogDebug("genhub.desktop is already up to date.");
                return;
            }

            File.WriteAllText(desktopFilePath, desiredContent);

            RunUpdateDesktopDatabase(appsDir, logger);
            RunXdgMimeDefault($"x-scheme-handler/{SchemeName}", logger);
            RunXdgMimeDefault("application/x-genhub-profile", logger);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to register genhub:// scheme on Linux");
        }
    }

    [SuppressMessage("Security", "S4036:Make sure the executable exists, and provide an absolute path or configure PATH securely", Justification = "Resolves update-desktop-database from known trusted absolute paths on Linux.")]
    private static void RunUpdateDesktopDatabase(string appsDir, ILogger? logger)
    {
        const string primaryPath = "/usr/bin/update-desktop-database";
        const string fallbackPath = "/usr/local/bin/update-desktop-database";

        string executablePath;
        if (File.Exists(primaryPath))
        {
            executablePath = primaryPath;
        }
        else if (File.Exists(fallbackPath))
        {
            executablePath = fallbackPath;
        }
        else
        {
            return;
        }

        if (!Path.IsPathRooted(executablePath))
        {
            return;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = executablePath,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add(appsDir);

            using var process = System.Diagnostics.Process.Start(psi);
            if (process != null && !process.WaitForExit(CommandTimeoutMs))
            {
                try
                {
                    process.Kill(true);
                }
                catch (Exception killEx) when (killEx is InvalidOperationException or Win32Exception)
                {
                    logger?.LogDebug(killEx, "update-desktop-database already exited before kill.");
                }

                logger?.LogWarning("update-desktop-database timed out after {TimeoutMs}ms and was terminated.", CommandTimeoutMs);
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            logger?.LogDebug(ex, "Failed to run update-desktop-database; binary may be absent or invocation failed.");
        }
    }

    [SuppressMessage("Security", "S4036:Make sure the executable exists, and provide an absolute path or configure PATH securely", Justification = "Resolves xdg-mime from known trusted absolute paths on Linux.")]
    private static void RunXdgMimeDefault(string mimeType, ILogger? logger)
    {
        const string primaryPath = "/usr/bin/xdg-mime";
        const string fallbackPath = "/usr/local/bin/xdg-mime";

        string executablePath;
        if (File.Exists(primaryPath))
        {
            executablePath = primaryPath;
        }
        else if (File.Exists(fallbackPath))
        {
            executablePath = fallbackPath;
        }
        else
        {
            return;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = executablePath,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("default");
            psi.ArgumentList.Add("genhub.desktop");
            psi.ArgumentList.Add(mimeType);

            using var process = System.Diagnostics.Process.Start(psi);
            if (process != null && !process.WaitForExit(CommandTimeoutMs))
            {
                try
                {
                    process.Kill(true);
                }
                catch (Exception killEx) when (killEx is InvalidOperationException or Win32Exception)
                {
                    logger?.LogDebug(killEx, "xdg-mime already exited before kill.");
                }
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            logger?.LogDebug(ex, "Failed to run xdg-mime default for {MimeType}.", mimeType);
        }
    }
}
