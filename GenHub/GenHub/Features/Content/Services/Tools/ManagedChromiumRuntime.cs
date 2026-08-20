using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GenHub.Features.Content.Services.Tools;

/// <summary>
/// Owns the app-local Playwright Chromium runtime. Playwright's NuGet package supplies the
/// driver but deliberately does not ship browser binaries, so GenHub provisions Chromium under
/// its application-data directory instead of relying on a system browser installation.
/// System Chrome/Edge cannot satisfy this requirement — Playwright needs its own patched build.
/// </summary>
internal sealed class ManagedChromiumRuntime(
    string runtimeDirectory,
    Func<string[], int> installer,
    Func<string, Task<bool>> requestInstallConsentAsync,
    ILogger logger,
    INotificationService? notificationService = null)
{
    /// <summary>
    /// Environment variable used by Playwright to locate app-owned browser binaries.
    /// </summary>
    internal const string BrowserPathEnvironmentVariable = "PLAYWRIGHT_BROWSERS_PATH";

    /// <summary>
    /// Environment variable used by Playwright to locate its driver binary (node.exe).
    /// </summary>
    internal const string DriverPathEnvironmentVariable = "PLAYWRIGHT_DRIVER_PATH";

    /// <summary>
    /// Configures Playwright to resolve browsers only from GenHub's managed runtime directory.
    /// </summary>
    public void ConfigureEnvironment()
    {
        Directory.CreateDirectory(runtimeDirectory);
        Environment.SetEnvironmentVariable(BrowserPathEnvironmentVariable, runtimeDirectory);
        EnsureDriverEnvironmentVariable();
    }

    /// <summary>
    /// Installs Chromium exactly once when the app-owned executable is unavailable,
    /// after the user confirms the download via the standard confirmation dialog.
    /// </summary>
    /// <param name="chromium">The Playwright Chromium browser type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous provisioning operation.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the user declines installation.</exception>
    /// <exception cref="InvalidOperationException">Thrown when Chromium could not be provisioned.</exception>
    public async Task EnsureInstalledAsync(IBrowserType chromium, CancellationToken cancellationToken)
    {
        ConfigureEnvironment();

        if (File.Exists(chromium.ExecutablePath))
        {
            return;
        }

        logger.LogInformation(
            "Managed Chromium is missing. Requesting user consent before installing under {RuntimeDirectory}",
            runtimeDirectory);

        var consented = await requestInstallConsentAsync(runtimeDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        if (!consented)
        {
            logger.LogInformation("User declined managed Chromium installation under {RuntimeDirectory}", runtimeDirectory);
            throw new OperationCanceledException(
                "ModDB requires GenHub's managed Chromium runtime. Installation was cancelled.");
        }

        logger.LogInformation("Managed Chromium install consented. Installing under {RuntimeDirectory}", runtimeDirectory);

        var toastId = Guid.NewGuid();
        if (notificationService != null)
        {
            notificationService.Show(new NotificationMessage(
                NotificationType.Info,
                ModDBConstants.ChromiumInstallTitle,
                ModDBConstants.ChromiumDownloadingMessage,
                autoDismissMilliseconds: null,
                actions: null,
                isPersistent: false,
                showInBadge: false)
            {
                Id = toastId,
            });
        }

        int exitCode = 0;
        using var progressCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var progressTask = Task.Run(async () =>
        {
            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                while (!progressCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(2000, progressCts.Token);
                    if (progressCts.Token.IsCancellationRequested)
                    {
                        break;
                    }

                    var elapsedSec = (int)stopwatch.Elapsed.TotalSeconds;

                    var hasExtractedDirs = Directory.Exists(runtimeDirectory) &&
                                           Directory.GetDirectories(runtimeDirectory).Length > 0;

                    string statusMessage = hasExtractedDirs
                        ? ModDBConstants.ChromiumExtractingMessage
                        : elapsedSec > 10
                            ? $"{ModDBConstants.ChromiumDownloadingMessage} ({elapsedSec}s)"
                            : ModDBConstants.ChromiumDownloadingMessage;

                    notificationService?.Update(toastId, statusMessage, ModDBConstants.ChromiumInstallTitle);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal termination when progress is cancelled
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Error updating Chromium install progress notification");
            }
        });

        try
        {
            exitCode = await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return installer(["install", "chromium"]);
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            progressCts.Cancel();
            await progressTask;
            notificationService?.Dismiss(toastId);
            throw;
        }
        catch (Exception ex)
        {
            progressCts.Cancel();
            await progressTask;
            notificationService?.Dismiss(toastId);
            notificationService?.ShowError(
                ModDBConstants.ChromiumInstallFailedTitle,
                ModDBConstants.ChromiumInstallFailedMessage,
                autoDismissMs: NotificationDurations.VeryLong);
            throw new InvalidOperationException(
                "GenHub could not install its managed Chromium runtime. Check the network connection and try the ModDB action again.",
                ex);
        }
        finally
        {
            progressCts.Cancel();
            await progressTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (exitCode != 0 || !File.Exists(chromium.ExecutablePath))
        {
            notificationService?.Dismiss(toastId);
            notificationService?.ShowError(
                ModDBConstants.ChromiumInstallFailedTitle,
                ModDBConstants.ChromiumInstallFailedMessage,
                autoDismissMs: NotificationDurations.VeryLong);
            throw new InvalidOperationException(
                "GenHub could not install its managed Chromium runtime. Check the network connection and try the ModDB action again.");
        }

        logger.LogInformation("Managed Chromium installation completed in {RuntimeDirectory}", runtimeDirectory);

        if (notificationService != null)
        {
            notificationService.Update(
                toastId,
                ModDBConstants.ChromiumReadyMessage,
                ModDBConstants.ChromiumReadyTitle);

            _ = Task.Delay(NotificationDurations.Medium).ContinueWith(_ =>
            {
                try
                {
                    notificationService.Dismiss(toastId);
                }
                catch
                {
                }
            });
        }
    }

    private static string GetPlatformFolder()
    {
        if (OperatingSystem.IsWindows())
        {
            return "win32_x64";
        }

        if (OperatingSystem.IsLinux())
        {
            return System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64
                ? "linux-arm64"
                : "linux-x64";
        }

        if (OperatingSystem.IsMacOS())
        {
            return System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64
                ? "darwin-arm64"
                : "darwin-x64";
        }

        return "win32_x64";
    }

    private static string? _cachedDriverPath;

    private void EnsureDriverEnvironmentVariable()
    {
        if (!string.IsNullOrWhiteSpace(_cachedDriverPath) && File.Exists(_cachedDriverPath))
        {
            Environment.SetEnvironmentVariable(DriverPathEnvironmentVariable, _cachedDriverPath);
            return;
        }

        var existingDriverPath = Environment.GetEnvironmentVariable(DriverPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(existingDriverPath) && File.Exists(existingDriverPath))
        {
            _cachedDriverPath = existingDriverPath;
            return;
        }

        var platformFolder = GetPlatformFolder();
        var nodeBinaryName = OperatingSystem.IsWindows() ? "node.exe" : "node";

        var searchDirectories = new[]
        {
            AppContext.BaseDirectory,
            AppDomain.CurrentDomain.BaseDirectory,
            Path.GetDirectoryName(typeof(ManagedChromiumRuntime).Assembly.Location) ?? string.Empty,
            Path.GetDirectoryName(Environment.ProcessPath) ?? string.Empty,
        };

        foreach (var dir in searchDirectories)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                continue;
            }

            var candidate = Path.Combine(dir, ".playwright", "node", platformFolder, nodeBinaryName);
            if (File.Exists(candidate))
            {
                _cachedDriverPath = candidate;
                Environment.SetEnvironmentVariable(DriverPathEnvironmentVariable, candidate);
                logger.LogInformation("Resolved Playwright driver path: {DriverPath}", candidate);
                return;
            }

            var current = new DirectoryInfo(dir);
            for (var depth = 0; depth < 4 && current?.Parent != null; depth++)
            {
                current = current.Parent;
                candidate = Path.Combine(current.FullName, ".playwright", "node", platformFolder, nodeBinaryName);
                if (File.Exists(candidate))
                {
                    _cachedDriverPath = candidate;
                    Environment.SetEnvironmentVariable(DriverPathEnvironmentVariable, candidate);
                    logger.LogInformation("Resolved Playwright driver path from parent directory: {DriverPath}", candidate);
                    return;
                }
            }
        }

        logger.LogWarning("Could not resolve Playwright driver node executable in any standard directory");
    }
}
