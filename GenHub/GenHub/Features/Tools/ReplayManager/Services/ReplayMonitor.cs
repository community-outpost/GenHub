using System;
using System.IO;
using System.Threading;
using GenHub.Core.Constants;
using GenHub.Core.Models.Events;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ReplayManager.Services;

/// <summary>
/// Monitors a replay file for completion using FileSystemWatcher and stability checks.
/// </summary>
/// <param name="logger">The logger instance.</param>
public sealed class ReplayMonitor(ILogger<ReplayMonitor> logger) : IDisposable
{
    private readonly object _lock = new();
    private FileSystemWatcher? _watcher;
    private Timer? _stabilityTimer;
    private long _lastFileSize;
    private int _stabilityCheckCount;
    private bool _isDisposed;

    /// <summary>
    /// Event raised when the replay file is complete and stable.
    /// </summary>
    public event EventHandler<ReplayFileCompletedEventArgs>? FileCompleted;

    /// <summary>
    /// Gets the file path being monitored.
    /// </summary>
    public string? FilePath { get; private set; }

    /// <summary>
    /// Gets a value indicating whether monitoring is active.
    /// </summary>
    public bool IsMonitoring { get; private set; }

    /// <summary>
    /// Starts monitoring a replay file.
    /// </summary>
    /// <param name="filePath">The file path to monitor.</param>
    public void StartMonitoring(string filePath)
    {
        lock (_lock)
        {
            if (_isDisposed)
            {
                logger.LogWarning("Cannot start monitoring on a disposed ReplayMonitor");
                return;
            }

            if (IsMonitoring)
            {
                logger.LogWarning("Already monitoring a file: {FilePath}", FilePath);
                return;
            }

            FilePath = filePath;
            _lastFileSize = 0;
            _stabilityCheckCount = 0;

            var directory = Path.GetDirectoryName(filePath);
            var fileName = Path.GetFileName(filePath);

            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
            {
                logger.LogError("Invalid file path: {FilePath}", filePath);
                return;
            }

            if (!Directory.Exists(directory))
            {
                logger.LogWarning("Directory does not exist, creating: {Directory}", directory);
                Directory.CreateDirectory(directory);
            }

            _watcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            };

            _watcher.Changed += OnFileChanged;
            _watcher.Error += OnWatcherError;
            _watcher.EnableRaisingEvents = true;

            IsMonitoring = true;

            logger.LogInformation("Started monitoring replay file: {FilePath}", filePath);

            // Don't start stability checks on pre-existing files
            // Wait for FileSystemWatcher to detect actual changes from the game
        }
    }

    /// <summary>
    /// Stops monitoring.
    /// </summary>
    public void StopMonitoring()
    {
        lock (_lock)
        {
            if (!IsMonitoring)
            {
                return;
            }

            IsMonitoring = false;

            _stabilityTimer?.Dispose();
            _stabilityTimer = null;

            if (_watcher != null)
            {
                _watcher.Changed -= OnFileChanged;
                _watcher.Error -= OnWatcherError;
                _watcher.Dispose();
                _watcher = null;
            }

            logger.LogInformation("Stopped monitoring replay file: {FilePath}", FilePath);

            FilePath = null;
            _lastFileSize = 0;
            _stabilityCheckCount = 0;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed)
            {
                return;
            }

            StopMonitoring();
            _isDisposed = true;
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        lock (_lock)
        {
            if (!IsMonitoring || !string.Equals(FilePath, e.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                var fileInfo = new FileInfo(e.FullPath);
                if (!fileInfo.Exists)
                {
                    return;
                }

                var currentSize = fileInfo.Length;

                if (currentSize != _lastFileSize)
                {
                    _lastFileSize = currentSize;
                    _stabilityCheckCount = 0;
                    StartStabilityCheck();

                    logger.LogDebug("Replay file changed: {FilePath} ({Size} bytes)", e.FullPath, currentSize);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error handling file change: {FilePath}", e.FullPath);
            }
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        lock (_lock)
        {
            logger.LogError(e.GetException(), "FileSystemWatcher error for: {FilePath}", FilePath);

            if (!IsMonitoring || _watcher == null)
            {
                return;
            }

            // Attempt to restart the watcher to recover from buffer overflow or similar errors
            try
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.EnableRaisingEvents = true;
                logger.LogInformation("Restarted FileSystemWatcher after error for: {FilePath}", FilePath);
            }
            catch (Exception restartEx)
            {
                logger.LogError(restartEx, "Failed to restart FileSystemWatcher for: {FilePath}", FilePath);
            }
        }
    }

    private void StartStabilityCheck()
    {
        _stabilityTimer?.Dispose();

        _stabilityTimer = new Timer(
            CheckFileStability,
            null,
            ReplayManagerConstants.FileStabilityCheckIntervalMs,
            Timeout.Infinite);
    }

    private void CheckFileStability(object? state)
    {
        string? completedFilePath = null;

        lock (_lock)
        {
            if (!IsMonitoring || string.IsNullOrEmpty(FilePath))
            {
                return;
            }

            try
            {
                var fileInfo = new FileInfo(FilePath);
                if (!fileInfo.Exists)
                {
                    return;
                }

                var currentSize = fileInfo.Length;

                if (currentSize == _lastFileSize && currentSize >= ReplayManagerConstants.MinimumReplayFileSizeBytes)
                {
                    _stabilityCheckCount++;

                    logger.LogDebug(
                        "Replay file stable check {Count}/{Required}: {FilePath} ({Size} bytes)",
                        _stabilityCheckCount,
                        ReplayManagerConstants.FileStabilityCheckCount,
                        FilePath,
                        currentSize);

                    if (_stabilityCheckCount >= ReplayManagerConstants.FileStabilityCheckCount)
                    {
                        logger.LogInformation("Replay file completed: {FilePath} ({Size} bytes)", FilePath, _lastFileSize);
                        completedFilePath = FilePath;
                        StopMonitoring();
                    }
                    else
                    {
                        _stabilityTimer?.Change(ReplayManagerConstants.FileStabilityCheckIntervalMs, Timeout.Infinite);
                    }
                }
                else
                {
                    _lastFileSize = currentSize;
                    _stabilityCheckCount = 0;
                    _stabilityTimer?.Change(ReplayManagerConstants.FileStabilityCheckIntervalMs, Timeout.Infinite);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error checking file stability: {FilePath}", FilePath);
            }
        }

        // Raise event outside _lock to avoid blocking the monitor while handlers execute
        if (completedFilePath != null)
        {
            FileCompleted?.Invoke(this, new ReplayFileCompletedEventArgs { FilePath = completedFilePath });
        }
    }
}
