using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.ReplayManager;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ReplayManager.Services;

/// <summary>
/// Supplies manifest IDs referenced by local replay files, protecting them from deletion during content cleanup.
/// </summary>
public class ReplayPinnedManifestProvider(
    ILogger<ReplayPinnedManifestProvider> logger,
    IReplayDirectoryService replayDirectoryService,
    ICrcMappingRegistry crcMappingRegistry) : IPinnedManifestProvider
{
    private readonly ILogger<ReplayPinnedManifestProvider> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IReplayDirectoryService _replayDirectoryService = replayDirectoryService ?? throw new ArgumentNullException(nameof(replayDirectoryService));
    private readonly ICrcMappingRegistry _crcMappingRegistry = crcMappingRegistry ?? throw new ArgumentNullException(nameof(crcMappingRegistry));

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> GetPinnedManifestIdsAsync(CancellationToken cancellationToken = default)
    {
        var pinnedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var gameType in new[] { GameType.ZeroHour, GameType.Generals })
        {
            try
            {
                var replays = await _replayDirectoryService.GetReplaysAsync(gameType, cancellationToken);
                foreach (var replay in replays)
                {
                    CollectPinnedIdsForReplay(replay, pinnedIds);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to inspect local replays for {GameType} to resolve pinned manifests", gameType);
            }
        }

        _logger.LogDebug("ReplayPinnedManifestProvider identified {Count} pinned manifests from replays", pinnedIds.Count);
        return pinnedIds;
    }

    private static void AddEntryManifests(CrcMappingEntry entry, HashSet<string> pinnedIds)
    {
        pinnedIds.Add(entry.ManifestId);
        if (!string.IsNullOrEmpty(entry.DataPatchManifestId))
        {
            pinnedIds.Add(entry.DataPatchManifestId);
        }
    }

    private void CollectPinnedIdsForReplay(ReplayFile replay, HashSet<string> pinnedIds)
    {
        if (!replay.ExeCrc.HasValue)
        {
            CollectIniOnlyPinnedIds(replay, pinnedIds);
            return;
        }

        CollectExePinnedIds(replay, pinnedIds);
    }

    private void CollectIniOnlyPinnedIds(ReplayFile replay, HashSet<string> pinnedIds)
    {
        if (replay.IniCrc.HasValue
            && _crcMappingRegistry.TryGetEntryByIniCrc($"0x{replay.IniCrc.Value:X8}", out var iniEntry)
            && !string.IsNullOrEmpty(iniEntry?.DataPatchManifestId))
        {
            pinnedIds.Add(iniEntry.DataPatchManifestId);
        }
    }

    private void CollectExePinnedIds(ReplayFile replay, HashSet<string> pinnedIds)
    {
        var exeHex = $"0x{replay.ExeCrc!.Value:X8}";
        var iniHex = replay.IniCrc.HasValue ? $"0x{replay.IniCrc.Value:X8}" : null;

        if (iniHex != null && _crcMappingRegistry.TryGetEntry(exeHex, iniHex, out var matchedEntry) && matchedEntry?.ManifestId != null)
        {
            AddEntryManifests(matchedEntry, pinnedIds);
            return;
        }

        if (iniHex != null
            && _crcMappingRegistry.TryGetEntryByIniCrc(iniHex, out var iniEntry)
            && !string.IsNullOrWhiteSpace(iniEntry?.DataPatchManifestId))
        {
            pinnedIds.Add(iniEntry.DataPatchManifestId);
        }

        if (_crcMappingRegistry.TryGetEntryByExeCrc(exeHex, out var exeEntry) && exeEntry?.ManifestId != null)
        {
            AddEntryManifests(exeEntry, pinnedIds);
            PinCandidateExeEntries(exeHex, pinnedIds);
        }
    }

    private void PinCandidateExeEntries(string exeHex, HashSet<string> pinnedIds)
    {
        var allEntries = _crcMappingRegistry.GetAllEntries();
        if (allEntries == null)
        {
            return;
        }

        var normalizedExe = CrcMappingRegistry.NormalizeHex(exeHex);
        foreach (var candidate in allEntries.Where(c => string.Equals(CrcMappingRegistry.NormalizeHex(c.ExeCrc), normalizedExe, StringComparison.OrdinalIgnoreCase)))
        {
            AddEntryManifests(candidate, pinnedIds);
        }
    }
}
