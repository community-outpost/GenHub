using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Online;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
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

            var configPath = await StageConfigAsync(configContents, cancellationToken);
            var process = CreateProcess(binary, locator.BuildArguments(configPath));
            var started = false;
            try
            {
                started = process.Start();
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                logger.LogWarning(ex, "Overlay sidecar spawn failed.");
            }

            if (!started)
            {
                process.Dispose();
                DeleteConfig(configPath);
                return OperationResult<SidecarInfo>.CreateFailure("Overlay sidecar failed to start.");
            }

            lock (_syncLock)
            {
                _process = process;
                _configPath = configPath;
            }

            DrainOutput(process);
            var survived = await WaitForStartupAsync(process, cancellationToken);
            if (!survived)
            {
                var exit = ReadExitCode(process);
                await StopInternalAsync();
                return OperationResult<SidecarInfo>.CreateFailure($"Overlay sidecar exited during startup (code {exit}).");
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
        lock (_syncLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _process?.Dispose();
            _process = null;
        }

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
        var directory = Path.Combine(Path.GetTempPath(), "genhub-online");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"overlay-{Guid.NewGuid():N}.json");
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

    private static Process CreateProcess(string binary, string arguments)
    {
        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = binary,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
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

    private static void DeleteConfig(string? configPath)
    {
        if (string.IsNullOrEmpty(configPath))
        {
            return;
        }

        try
        {
            File.Delete(configPath);
        }
        catch (IOException)
        {
            // Best effort cleanup of staged config.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup of staged config.
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
            catch (InvalidOperationException)
            {
                // Already exited.
            }
        });

        await process.WaitForExitAsync(cancellationToken);
        registration.Unregister();
    }

    private static async Task<bool> WaitForStartupAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(OnlineConstants.SidecarStartupGraceMs, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        return !process.HasExited;
    }

    private void DrainOutput(Process process)
    {
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
                catch (OperationCanceledException)
                {
                    logger.LogWarning("Overlay sidecar did not exit in time.");
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
}
