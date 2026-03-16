using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Events;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ReplayManager.Services;

/// <summary>
/// Service for orchestrating replay monitoring and automatic saving.
/// </summary>
/// <param name="directoryService">The directory service.</param>
/// <param name="saveService">The save service.</param>
/// <param name="loggerFactory">The logger factory.</param>
/// <param name="logger">The logger instance.</param>
public sealed class ReplayMonitoringService(
    IReplayDirectoryService directoryService,
    ReplaySaveService saveService,
    ILoggerFactory loggerFactory,
    ILogger<ReplayMonitoringService> logger) : IReplayMonitorService, IDisposable
{
    private readonly ConcurrentDictionary<string, MonitoringSession> _activeSessions = new();
    private volatile bool _isDisposed;

    /// <inheritdoc/>
    public event EventHandler<ReplayCompletedEventArgs>? ReplayCompleted;

    /// <inheritdoc/>
    public Task StartMonitoringAsync(string profileId, GameType gameType)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        // Stop existing monitoring for this profile
        StopMonitoringInternal(profileId);

        var replayDirectory = directoryService.GetReplayDirectory(gameType);
        var replayFilePath = Path.Combine(replayDirectory, ReplayManagerConstants.DefaultReplayFileName);

        var monitor = new ReplayMonitor(loggerFactory.CreateLogger<ReplayMonitor>());
        var sessionId = Guid.NewGuid().ToString();

        monitor.FileCompleted += async (sender, args) =>
        {
            try
            {
                await OnReplayFileCompletedAsync(profileId, gameType, sessionId, args);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled exception in FileCompleted event handler for profile: {ProfileId}", profileId);
            }
        };

        var session = new MonitoringSession
        {
            SessionId = sessionId,
            ProfileId = profileId,
            GameType = gameType,
            Monitor = monitor,
            ReplayFilePath = replayFilePath,
        };

        if (!_activeSessions.TryAdd(profileId, session))
        {
            monitor.Dispose();
            logger.LogWarning("Failed to add monitoring session for profile: {ProfileId}", profileId);
            return Task.CompletedTask;
        }

        try
        {
            monitor.StartMonitoring(replayFilePath);
        }
        catch
        {
            _activeSessions.TryRemove(new KeyValuePair<string, MonitoringSession>(profileId, session));
            monitor.Dispose();
            throw;
        }

        logger.LogInformation(
            "Started replay monitoring for profile {ProfileId} ({GameType}) session {SessionId}: {FilePath}",
            profileId,
            gameType,
            sessionId,
            replayFilePath);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopMonitoringAsync(string profileId)
    {
        return StopMonitoringAsync(profileId, sessionId: null);
    }

    /// <summary>
    /// Stops monitoring for a specific profile, optionally verifying session identity.
    /// </summary>
    /// <param name="profileId">The profile ID.</param>
    /// <param name="sessionId">If provided, only stops if the active session matches this ID.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task StopMonitoringAsync(string profileId, string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return Task.CompletedTask;
        }

        if (sessionId != null)
        {
            StopMonitoringInternal(profileId, sessionId);
        }
        else
        {
            StopMonitoringInternal(profileId);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAllMonitoringAsync()
    {
        foreach (var profileId in _activeSessions.Keys.ToArray())
        {
            StopMonitoringInternal(profileId);
        }

        logger.LogInformation("Stopped all replay monitoring");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public bool IsMonitoring(string profileId)
    {
        return !string.IsNullOrWhiteSpace(profileId) && _activeSessions.ContainsKey(profileId);
    }

    /// <summary>
    /// Gets the session ID for an active monitoring session.
    /// </summary>
    /// <param name="profileId">The profile ID.</param>
    /// <returns>The session ID, or null if not monitoring.</returns>
    public string? GetSessionId(string profileId)
    {
        return _activeSessions.TryGetValue(profileId, out var session) ? session.SessionId : null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        foreach (var session in _activeSessions.Values.ToArray())
        {
            try
            {
                session.Monitor.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error disposing monitor for session: {SessionId}", session.SessionId);
            }
        }

        _activeSessions.Clear();
    }

    private void StopMonitoringInternal(string profileId)
    {
        if (_activeSessions.TryRemove(profileId, out var session))
        {
            session.Monitor.Dispose();
            logger.LogInformation("Stopped replay monitoring for profile: {ProfileId} session: {SessionId}", profileId, session.SessionId);
        }
    }

    private void StopMonitoringInternal(string profileId, string expectedSessionId)
    {
        // Optimistic path: only attempt removal if the session matches
        if (!_activeSessions.TryGetValue(profileId, out var existingSession) ||
            existingSession?.SessionId != expectedSessionId)
        {
            logger.LogInformation(
                "Skipping stop for profile {ProfileId}: session {ExpectedSession} not found or replaced",
                profileId,
                expectedSessionId);
            return;
        }

        // TryRemove with KeyValuePair is atomic - only removes if both key and value match
        // This prevents removing a newer session inserted by concurrent StartMonitoringAsync
        if (_activeSessions.TryRemove(new KeyValuePair<string, MonitoringSession>(profileId, existingSession)))
        {
            existingSession.Monitor.Dispose();
            logger.LogInformation(
                "Stopped replay monitoring for profile: {ProfileId} session: {SessionId}",
                profileId,
                existingSession.SessionId);
        }
        else
        {
            // Session was replaced between TryGetValue and TryRemove
            logger.LogInformation(
                "Skipping stop for profile {ProfileId}: session {ExpectedSession} was replaced during removal",
                profileId,
                expectedSessionId);
        }
    }

    private async Task OnReplayFileCompletedAsync(string profileId, GameType gameType, string sessionId, ReplayFileCompletedEventArgs args)
    {
        try
        {
            logger.LogInformation(
                "Replay file completed for profile {ProfileId}: {FilePath}",
                profileId,
                args.FilePath);

            var (savedFilePath, metadata) = await saveService.SaveReplayAsync(args.FilePath, gameType);

            if (string.IsNullOrEmpty(savedFilePath) || metadata == null)
            {
                logger.LogWarning("Failed to save replay for profile: {ProfileId}", profileId);
                return;
            }

            ReplayCompleted?.Invoke(this, new ReplayCompletedEventArgs
            {
                ProfileId = profileId,
                SavedFilePath = savedFilePath,
                Metadata = metadata,
            });

            logger.LogInformation(
                "Successfully saved replay for profile {ProfileId}: {SavedFilePath}",
                profileId,
                savedFilePath);

            // Only stop monitoring after successful save
            StopMonitoringInternal(profileId, sessionId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling replay completion for profile: {ProfileId}", profileId);
        }
    }

    private sealed class MonitoringSession
    {
        public required string SessionId { get; init; }

        public required string ProfileId { get; init; }

        public required GameType GameType { get; init; }

        public required ReplayMonitor Monitor { get; init; }

        public required string ReplayFilePath { get; init; }
    }
}
