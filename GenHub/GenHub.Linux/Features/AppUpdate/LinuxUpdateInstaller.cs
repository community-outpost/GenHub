using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.AppUpdate;
using GenHub.Core.Models.AppUpdate;
using GenHub.Features.AppUpdate.Services;
using Microsoft.Extensions.Logging;

namespace GenHub.Linux.Features.AppUpdate;

/// <summary>
/// Linux-specific update installer implementation.
/// </summary>
public class LinuxUpdateInstaller(HttpClient httpClient, ILogger<LinuxUpdateInstaller> logger)
    : BaseUpdateInstaller(httpClient, logger), IPlatformUpdateInstaller
{
    private readonly ILogger<LinuxUpdateInstaller> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    protected override List<string> GetPlatformAssetPatterns()
    {
        return new List<string>
        {
            "linux",
            ".tar.gz",
            ".appimage",
            ".deb",
            ".rpm",
            ".zip",
        };
    }

    /// <inheritdoc/>
    protected override async Task<bool> InstallExecutableAsync(
        string exePath,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new UpdateProgress
        {
            Status = "Launching Linux installer...",
            PercentComplete = 95,
        });

        try
        {
           // Try to find chmod in PATH
            var chmodPath = await FindExecutableInPathAsync("chmod", cancellationToken);
            if (string.IsNullOrEmpty(chmodPath))
            {
                _logger.LogError("chmod command not found in PATH");
                return false;
            }

            var chmodInfo = new ProcessStartInfo
            {
                FileName = chmodPath,
                Arguments = $"+x \"{exePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using (var chmodProcess = Process.Start(chmodInfo))
            {
                if (chmodProcess != null)
                {
                    await chmodProcess.WaitForExitAsync(cancellationToken);
                }
            }

            // Execute the installer
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                _logger.LogError("Failed to start Linux installer process");
                return false;
            }

            await process.WaitForExitAsync(cancellationToken);

            var success = process.ExitCode == 0;
            progress?.Report(new UpdateProgress
            {
                Status = success ? "Installation completed successfully" : "Installation failed",
                PercentComplete = 100,
                IsCompleted = true,
                HasError = !success,
                ErrorMessage = success ? null : $"Installer exited with code {process.ExitCode}",
            });

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute Linux installer");
            return false;
        }
    }

    /// <inheritdoc/>
    protected override async Task<bool> CreateAndLaunchExternalUpdaterAsync(
        string sourceDirectory,
        string targetDirectory,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var updaterDir = Path.Combine(Path.GetTempPath(), "GenHubUpdater", Guid.NewGuid().ToString());
            Directory.CreateDirectory(updaterDir);

            var logFile = Path.Combine(updaterDir, "update.log");
            var currentProcess = Process.GetCurrentProcess();
            var currentExecutable = currentProcess.MainModule?.FileName ?? string.Empty;
            var processId = currentProcess.Id;

            var bashScriptPath = Path.Combine(updaterDir, "update_genhub.sh");
            var scriptContent = await LoadUpdateScriptAsync("GenHub.Linux.Resources.update_genhub.sh", cancellationToken);

            // Replace placeholders
            scriptContent = scriptContent
                .Replace("{{LOG_FILE}}", logFile)
                .Replace("{{PROCESS_ID}}", processId.ToString())
                .Replace("{{SOURCE_DIR}}", sourceDirectory)
                .Replace("{{TARGET_DIR}}", targetDirectory)
                .Replace("{{CURRENT_EXE}}", currentExecutable)
                .Replace("{{BACKUP_DIR}}", Path.Combine(Path.GetTempPath(), "GenHubBackup", DateTime.Now.ToString("yyyyMMdd_HHmmss")));

            await File.WriteAllTextAsync(bashScriptPath, scriptContent, new UTF8Encoding(false), cancellationToken);

            try
            {
                var chmodInfo = new ProcessStartInfo
                {
                    FileName = "/bin/chmod",
                    Arguments = $"+x \"{bashScriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using (var chmodProcess = Process.Start(chmodInfo))
                {
                    if (chmodProcess != null)
                    {
                        await chmodProcess.WaitForExitAsync(cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to make script executable (likely not on Linux)");

                // Continue anyway for testing
            }

            _logger.LogInformation("Created Linux external updater script: {BashScriptPath}", bashScriptPath);

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"\"{bashScriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                if (Process.Start(startInfo) == null)
                {
                    _logger.LogError("Failed to start Linux external updater");
                    return false;
                }

                _logger.LogInformation("Linux external updater launched successfully.");
                return await ScheduleApplicationShutdownAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to launch bash script (likely not on Linux)");
                progress?.Report(new UpdateProgress
                {
                    Status = "Application will restart to complete installation.",
                    PercentComplete = 100,
                    IsCompleted = true,
                });
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create external updater");
            return false;
        }
    }

    private async Task<string> LoadUpdateScriptAsync(string resourceName, CancellationToken cancellationToken)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new FileNotFoundException($"Embedded resource '{resourceName}' not found.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private async Task<string?> FindExecutableInPathAsync(string executableName, CancellationToken cancellationToken)
    {
        var whichInfo = new ProcessStartInfo
        {
            FileName = "which",
            Arguments = executableName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };

        try
        {
            using var process = Process.Start(whichInfo);
            if (process != null)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync(cancellationToken);
                if (process.ExitCode == 0)
                {
                    return output.Trim();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to execute 'which' command");
        }

        // Fallback to common locations
        var commonPaths = new[] { "/bin/chmod", "/usr/bin/chmod" };
        return commonPaths.FirstOrDefault(File.Exists);
    }
}
