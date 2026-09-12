using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
    ILogger logger)
{
    /// <summary>
    /// Environment variable used by Playwright to locate app-owned browser binaries.
    /// </summary>
    internal const string BrowserPathEnvironmentVariable = "PLAYWRIGHT_BROWSERS_PATH";

    /// <summary>
    /// Environment variable used by Playwright to locate its driver binary (node.exe).
    /// </summary>
    internal const string DriverPathEnvironmentVariable = "PLAYWRIGHT_DRIVER_PATH";

    private readonly SemaphoreSlim _installLock = new(1, 1);
    private string? _cachedDriverPath;

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
    /// Note that cooperative cancellation is honored before and after the install process.
    /// </summary>
    /// <param name="chromium">The Playwright Chromium browser type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous provisioning operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the user declines installation or when Chromium could not be provisioned.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the cancellation token is requested.</exception>
    public async Task EnsureInstalledAsync(IBrowserType chromium, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chromium);
        ConfigureEnvironment();

        if (File.Exists(chromium.ExecutablePath))
        {
            return;
        }

        await _installLock.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(chromium.ExecutablePath))
            {
                return;
            }

            logger.LogDebug(
                "Managed Chromium is missing. Requesting user consent before installing under {RuntimeDirectory}",
                runtimeDirectory);

            var consented = await requestInstallConsentAsync(runtimeDirectory);
            cancellationToken.ThrowIfCancellationRequested();

            if (!consented)
            {
                logger.LogDebug("User declined managed Chromium installation under {RuntimeDirectory}", runtimeDirectory);
                throw new InvalidOperationException(
                    "ModDB requires GenHub's managed Chromium runtime. The installation was declined.");
            }

            logger.LogDebug("Managed Chromium install consented. Installing under {RuntimeDirectory}", runtimeDirectory);

            int exitCode = 0;
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
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "GenHub could not install its managed Chromium runtime. Check the network connection and try the ModDB action again.",
                    ex);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (exitCode != 0 || !File.Exists(chromium.ExecutablePath))
            {
                throw new InvalidOperationException(
                    "GenHub could not install its managed Chromium runtime. Check the network connection and try the ModDB action again.");
            }

            logger.LogInformation("Managed Chromium installation completed in {RuntimeDirectory}", runtimeDirectory);
        }
        finally
        {
            _installLock.Release();
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

    private static string? FindDriverCandidateInDirectory(string dir, string platformFolder, string nodeBinaryName)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            return null;
        }

        var candidate = Path.Combine(dir, ".playwright", "node", platformFolder, nodeBinaryName);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        var current = new DirectoryInfo(dir);
        for (var depth = 0; depth < 4 && current.Parent != null; depth++)
        {
            current = current.Parent;
            candidate = Path.Combine(current.FullName, ".playwright", "node", platformFolder, nodeBinaryName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

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
            var candidate = FindDriverCandidateInDirectory(dir, platformFolder, nodeBinaryName);
            if (candidate != null)
            {
                _cachedDriverPath = candidate;
                Environment.SetEnvironmentVariable(DriverPathEnvironmentVariable, candidate);
                logger.LogInformation("Resolved Playwright driver path: {DriverPath}", candidate);
                return;
            }
        }

        logger.LogWarning("Could not resolve Playwright driver node executable in any standard directory");
    }
}
