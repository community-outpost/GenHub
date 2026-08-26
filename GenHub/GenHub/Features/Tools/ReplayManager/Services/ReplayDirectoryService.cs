using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Tools.ReplayManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ReplayManager.Services;

/// <summary>
/// Implementation of <see cref="IReplayDirectoryService"/> for managing replay files on disk.
/// Automatically parses replay headers and resolves game client compatibility against installed content.
/// </summary>
public sealed class ReplayDirectoryService(
    IReplayHeaderParser headerParser,
    ICrcMappingRegistry crcMappingRegistry,
    IServiceScopeFactory scopeFactory,
    ILogger<ReplayDirectoryService> logger) : IReplayDirectoryService
{
    /// <inheritdoc />
    public string GetReplayDirectory(GameType version)
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var gameDataFolder = version switch
        {
            GameType.Generals => GameSettingsConstants.FolderNames.Generals,
            GameType.ZeroHour => GameSettingsConstants.FolderNames.ZeroHour,
            _ => throw new ArgumentException("Unsupported game version", nameof(version)),
        };

        return Path.Combine(documents, gameDataFolder, GameSettingsConstants.FolderNames.Replays);
    }

    /// <inheritdoc />
    public void EnsureDirectoryExists(GameType version)
    {
        var path = GetReplayDirectory(version);
        if (!Directory.Exists(path))
        {
            logger.LogInformation(LogMessages.CreatingReplayDirectory, path);
            Directory.CreateDirectory(path);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReplayFile>> GetReplaysAsync(GameType version, CancellationToken ct = default)
    {
        var directory = GetReplayDirectory(version);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var files = Directory.GetFiles(directory, "*.*")
            .Where(f => f.EndsWith(".rep", StringComparison.OrdinalIgnoreCase) ||
                       f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var acquiredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var scope = scopeFactory.CreateScope();
            var manifestPool = scope.ServiceProvider.GetRequiredService<IContentManifestPool>();
            var manifestsResult = await manifestPool.GetAllManifestsAsync(ct);
            if (manifestsResult.Success && manifestsResult.Data != null)
            {
                foreach (var manifest in manifestsResult.Data)
                {
                    acquiredIds.Add(manifest.Id.Value);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to retrieve acquired manifests for replay compatibility matching.");
        }

        var replayFiles = new List<ReplayFile>();
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var info = new FileInfo(file);
            var replay = new ReplayFile
            {
                FullPath = file,
                FileName = Path.GetFileName(file),
                SizeInBytes = info.Length,
                LastModified = info.LastWriteTime,
                GameVersion = version,
            };

            if (file.EndsWith(".rep", StringComparison.OrdinalIgnoreCase))
            {
                var parseResult = await headerParser.ParseHeaderAsync(file, ct);
                if (parseResult.Success && parseResult.Data != null)
                {
                    replay.Metadata = parseResult.Data;
                    ResolveCompatibility(replay, acquiredIds);
                }
            }

            replayFiles.Add(replay);
        }

        return replayFiles.OrderByDescending(r => r.LastModified).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<bool> DeleteReplaysAsync(IEnumerable<ReplayFile> replays, CancellationToken ct = default)
    {
        return await Task.Run(
            () =>
            {
                var success = true;
                foreach (var replay in replays)
                {
                    try
                    {
                        if (File.Exists(replay.FullPath))
                        {
                            File.Delete(replay.FullPath);
                            logger.LogInformation(LogMessages.DeletedReplay, replay.FullPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, LogMessages.FailedToDeleteReplay, replay.FullPath);
                        success = false;
                    }
                }

                return success;
            },
            ct);
    }

    /// <inheritdoc />
    [SuppressMessage("Security", "S4036:Command path should not be passed without validation", Justification = "Windows explorer launcher with absolute path.")]
    public void OpenInExplorer(GameType version)
    {
        var path = GetReplayDirectory(version);
        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = PlatformConstants.WindowsExplorerExecutable,
                Arguments = path,
                UseShellExecute = true,
            });
        }
    }

    /// <inheritdoc />
    [SuppressMessage("Security", "S4036:Command path should not be passed without validation", Justification = "Windows explorer selection launcher with absolute file path.")]
    public void RevealInExplorer(ReplayFile replay)
    {
        if (File.Exists(replay.FullPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = PlatformConstants.WindowsExplorerExecutable,
                Arguments = string.Format(PlatformConstants.WindowsExplorerSelectArgument, replay.FullPath),
                UseShellExecute = true,
            });
        }
    }

    private void ResolveCompatibility(ReplayFile replay, HashSet<string> acquiredIds)
    {
        if (replay.Metadata == null)
        {
            replay.CompatibilityStatus = ReplayCompatibilityStatus.Unknown;
            return;
        }

        var exeCrcStr = replay.Metadata.FormattedExeCrc;
        var iniCrcStr = replay.Metadata.FormattedIniCrc;

        if (string.IsNullOrEmpty(exeCrcStr))
        {
            replay.CompatibilityStatus = ReplayCompatibilityStatus.Unknown;
            return;
        }

        if (crcMappingRegistry.TryGetEntry(exeCrcStr, iniCrcStr ?? string.Empty, out var match) && match != null)
        {
            replay.MatchedClient = match;

            var isInstalled = !string.IsNullOrEmpty(match.ManifestId) && acquiredIds.Contains(match.ManifestId);

            if (isInstalled)
            {
                replay.CompatibilityStatus = ReplayCompatibilityStatus.Compatible;
            }
            else if (!string.IsNullOrWhiteSpace(match.CdnUrl))
            {
                replay.CompatibilityStatus = ReplayCompatibilityStatus.Downloadable;
            }
            else
            {
                replay.CompatibilityStatus = ReplayCompatibilityStatus.Orphaned;
            }
        }
        else
        {
            replay.CompatibilityStatus = ReplayCompatibilityStatus.Orphaned;
        }
    }
}
