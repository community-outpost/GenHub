using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Online;
using GenHub.Core.Models.Online;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Online.Services;

/// <summary>
/// Manages one overlay sidecar process: stages configuration to a temp file,
/// spawns the platform-located binary, drains scrubbed logs, and tears down
/// the process tree plus staged files on stop.
/// </summary>
/// <param name="logger">The logger.</param>
public sealed class OverlaySidecarHost(ILogger<OverlaySidecarHost> logger) : IOverlaySidecarHost, IDisposable
{
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly object _syncLock = new();

    private Process? _process;
    private string? _configPath;
    private bool _disposed;

    /// <inheritdoc/>
    public bool IsRunning
    {
        get
        {
            lock (_syncLock)
            {
                return _process is not null && !_process.HasExited;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<SidecarInfo>> StartAsync(
        string configContents,
        IOverlaySidecarLocator locator,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(locator);

        if (string.IsNullOrWhiteSpace(configContents))
        {
            return OperationResult<SidecarInfo>.CreateFailure("Overlay configuration is empty.");
        }

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (IsRunning)
            {
                return OperationResult<SidecarInfo>.CreateFailure("Overlay sidecar is already running.");
            }

            var binary = ResolveBinary(locator.LocateBinary());
            if (binary is null)
            {
                return OperationResult<SidecarInfo>.CreateFailure("Overlay sidecar is not installed.");
            }

            string? overlayIp = null;
            var parsed = OverlaySidecarConfig.Parse(configContents);
            if (parsed.Success && parsed.Data is not null)
            {
                overlayIp = parsed.Data.OverlayIp;
            }

            var configPath = await StageConfigAsync(configContents, cancellationToken);
            var process = CreateProcess(binary, locator.BuildArguments(configPath, overlayIp));
            var started = false;
            string? startError = null;
            try
            {
                started = process.Start();
            }
            catch (System.ComponentModel.Win32Exception winEx)
            {
                logger.LogWarning(winEx, "Overlay sidecar elevation or start failed.");
                startError = winEx.NativeErrorCode == 1223
                    ? "Administrator privileges are required to create the network adapter on Windows."
                    : $"Failed to start overlay sidecar: {winEx.Message}";
            }
            catch (Exception ex) when (ex is InvalidOperationException)
            {
                logger.LogWarning(ex, "Overlay sidecar spawn failed.");
                startError = $"Failed to start overlay sidecar: {ex.Message}";
            }

            if (!started)
            {
                process.Dispose();
                DeleteConfig(configPath);
                return OperationResult<SidecarInfo>.CreateFailure(startError ?? "Overlay sidecar failed to start.");
            }

            lock (_syncLock)
            {
                _process = process;
                _configPath = configPath;
            }

            DrainOutput(process);
            var survived = await WaitForStartupAsync(process, configPath, cancellationToken);
            if (!survived)
            {
                var exit = ReadExitCode(process);
                var errPath = configPath + ".err";
                string? specificError = null;
                if (File.Exists(errPath))
                {
                    try
                    {
                        specificError = File.ReadAllText(errPath).Trim();
                        File.Delete(errPath);
                    }
                    catch
                    {
                        // Best effort
                    }
                }

                await StopInternalAsync();
                var errorMessage = !string.IsNullOrWhiteSpace(specificError)
                    ? specificError
                    : $"Overlay sidecar exited during startup (code {exit}).";
                return OperationResult<SidecarInfo>.CreateFailure(errorMessage);
            }

            return OperationResult<SidecarInfo>.CreateSuccess(new SidecarInfo(process.Id, configPath));
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> StopAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            await StopInternalAsync();
            return OperationResult<bool>.CreateSuccess(true);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Process? process;
        string? configPath;
        lock (_syncLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            process = _process;
            configPath = _configPath;
            _process = null;
            _configPath = null;
        }

        // DI container disposal only calls Dispose: terminate the sidecar
        // here so it never outlives the host with staged TURN credentials.
        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    process.WaitForExit(OnlineConstants.SidecarStopTimeoutMs);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already exited or access denied during teardown.
            }
            finally
            {
                process.Dispose();
            }
        }

        DeleteConfig(configPath);
        _stateLock.Dispose();
    }

    private static string? ResolveBinary(string? located)
    {
        if (string.IsNullOrWhiteSpace(located))
        {
            return null;
        }

        if (File.Exists(located))
        {
            return located;
        }

        if (Path.IsPathRooted(located) || located.Contains(Path.DirectorySeparatorChar))
        {
            return null;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [string.Empty];

        foreach (var directory in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, located + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static async Task<string> StageConfigAsync(string configContents, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), OnlineConstants.SidecarConfigDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{OnlineConstants.SidecarConfigPrefix}{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, configContents, Encoding.UTF8, cancellationToken);

        // The staged config carries TURN credentials; restrict it to the owner on Unix.
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (IOException)
            {
                // Best effort hardening; the file is still short-lived.
            }
            catch (UnauthorizedAccessException)
            {
                // Best effort hardening; the file is still short-lived.
            }
        }

        return path;
    }

    private static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static Process CreateProcess(string binary, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = binary,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(binary) ?? string.Empty,
        };

        if (OperatingSystem.IsWindows() && !IsAdministrator())
        {
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            psi.WindowStyle = ProcessWindowStyle.Hidden;
        }
        else
        {
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
        }

        return new Process
        {
            StartInfo = psi,
            EnableRaisingEvents = true,
        };
    }

    private static int ReadExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : -1;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private static async Task WaitForExitAsync(Process process, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill(true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already exited or access denied during teardown.
            }
        });

        await process.WaitForExitAsync(cancellationToken);
        registration.Unregister();
    }

    private static async Task<bool> WaitForStartupAsync(Process process, string configPath, CancellationToken cancellationToken)
    {
        var readyFile = configPath + ".ready";
        var maxWaitMs = OnlineConstants.SidecarStartupGraceMs + (OperatingSystem.IsWindows() ? 4000 : 0);
        var deadline = DateTime.UtcNow.AddMilliseconds(maxWaitMs);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (process.HasExited)
            {
                return false;
            }

            if (File.Exists(readyFile))
            {
                return true;
            }

            try
            {
                await Task.Delay(100, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return !process.HasExited;
    }

    private void DrainOutput(Process process)
    {
        if (!process.StartInfo.RedirectStandardOutput || !process.StartInfo.RedirectStandardError)
        {
            return;
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                logger.LogDebug("Overlay: {Line}", OnlineLogScrubber.Scrub(e.Data));
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                logger.LogWarning("Overlay: {Line}", OnlineLogScrubber.Scrub(e.Data));
            }
        };

        try
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (InvalidOperationException)
        {
            // Process already exited; startup check reports it.
        }
    }

    private async Task StopInternalAsync()
    {
        Process? process;
        string? configPath;
        lock (_syncLock)
        {
            process = _process;
            configPath = _configPath;
            _process = null;
            _configPath = null;
        }

        if (process is null)
        {
            DeleteConfig(configPath);
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                using var timeout = new CancellationTokenSource(OnlineConstants.SidecarStopTimeoutMs);
                try
                {
                    await WaitForExitAsync(process, timeout.Token);
                }
                catch (OperationCanceledException ex)
                {
                    logger.LogWarning(ex, "Overlay sidecar did not exit in time.");
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        finally
        {
            process.Dispose();
            DeleteConfig(configPath);
        }
    }

    private void ThrowIfDisposed()
    {
        lock (_syncLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }

    private void DeleteConfig(string? configPath)
    {
        if (string.IsNullOrEmpty(configPath))
        {
            return;
        }

        try
        {
            if (File.Exists(configPath))
            {
                File.Delete(configPath);
            }

            var readyPath = configPath + ".ready";
            if (File.Exists(readyPath))
            {
                File.Delete(readyPath);
            }

            var errPath = configPath + ".err";
            if (File.Exists(errPath))
            {
                File.Delete(errPath);
            }
        }
        catch (IOException ex)
        {
            // The sidecar may still hold the credential file open after a
            // kill timeout; retry once in the background instead of leaving
            // TURN credentials in temp indefinitely.
            logger.LogWarning(ex, "Staged overlay config still locked; retrying deletion in the background.");
            _ = DeleteConfigLaterAsync(configPath);
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup of staged config.
        }
    }

    private async Task DeleteConfigLaterAsync(string configPath)
    {
        try
        {
            await Task.Delay(OnlineConstants.SidecarConfigDeleteRetryDelayMs);
            if (File.Exists(configPath))
            {
                File.Delete(configPath);
            }

            var readyPath = configPath + ".ready";
            if (File.Exists(readyPath))
            {
                File.Delete(readyPath);
            }

            var errPath = configPath + ".err";
            if (File.Exists(errPath))
            {
                File.Delete(errPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            logger.LogWarning(ex, "Background deletion of staged overlay config failed.");
        }
    }
}
