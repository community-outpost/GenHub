using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GeneralsOnline;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Tools.Checksum;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.GeneralsOnline.Services;

/// <summary>
/// Matches local game profiles against Generals Online lobby CRC values.
/// Profile CRCs are hashed through the shared game CRC calculator and cached
/// per profile until invalidated.
/// </summary>
/// <param name="profileManager">The game profile manager.</param>
/// <param name="logger">The logger instance.</param>
/// <param name="crcCalculator">The optional game CRC calculator service. Null degrades matches to unknown.</param>
/// <param name="installationService">The optional game installation service resolving install roots.</param>
/// <param name="manifestPool">The optional content manifest pool for content type lookups.</param>
/// <param name="timeProvider">The optional clock for cache expiry. Defaults to <see cref="TimeProvider.System"/>.</param>
public sealed class GeneralsOnlineCompatibilityService(
    IGameProfileManager profileManager,
    ILogger<GeneralsOnlineCompatibilityService> logger,
    IGameCrcCalculatorService? crcCalculator = null,
    IGameInstallationService? installationService = null,
    IContentManifestPool? manifestPool = null,
    TimeProvider? timeProvider = null) : IGeneralsOnlineCompatibilityService
{
    private sealed record ProfileCrcs(uint? ExeCrc, uint? IniCrc);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, (ProfileCrcs Crcs, DateTime CachedAtUtc)> _crcCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, ContentType>> _contentTypeCache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _computeLock = new(1, 1);

    /// <inheritdoc />
    public async Task<OperationResult<GeneralsOnlineProfileMatch>> MatchProfileAsync(
        GameProfile profile,
        GeneralsOnlineLobby lobby,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(lobby);

        var crcs = await ResolveProfileCrcsAsync(profile, cancellationToken);
        var compatibility = Evaluate(crcs, lobby.ExeCrc, lobby.IniCrc);
        logger.LogDebug(
            "Generals Online match for profile {ProfileId} against lobby {LobbyId}: {Compatibility} (profile exe {ProfileExe} ini {ProfileIni}, lobby exe {LobbyExe} ini {LobbyIni}).",
            profile.Id,
            lobby.LobbyId,
            compatibility,
            FormatCrc(crcs.ExeCrc),
            FormatCrc(crcs.IniCrc),
            FormatLobbyCrc(lobby.ExeCrc),
            FormatLobbyCrc(lobby.IniCrc));
        return OperationResult<GeneralsOnlineProfileMatch>.CreateSuccess(
            new GeneralsOnlineProfileMatch(profile.Id, profile.Name, compatibility, crcs.ExeCrc, crcs.IniCrc));
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<GeneralsOnlineProfileMatch>>> RankProfilesAsync(
        GeneralsOnlineLobby lobby,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lobby);

        var profiles = await profileManager.GetAllProfilesAsync(cancellationToken);
        if (!profiles.Success || profiles.Data is null)
        {
            return OperationResult<IReadOnlyList<GeneralsOnlineProfileMatch>>.CreateFailure(profiles.Errors);
        }

        var matches = new List<GeneralsOnlineProfileMatch>(profiles.Data.Count);
        foreach (var profile in profiles.Data)
        {
            var match = await MatchProfileAsync(profile, lobby, cancellationToken);
            if (match.Success && match.Data is not null)
            {
                matches.Add(match.Data);
            }
        }

        var ranked = matches
            .OrderBy(m => RankOf(m.Compatibility))
            .ThenBy(m => m.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return OperationResult<IReadOnlyList<GeneralsOnlineProfileMatch>>.CreateSuccess(ranked);
    }

    /// <inheritdoc />
    public void InvalidateCache(string? profileId = null)
    {
        if (profileId is null)
        {
            _crcCache.Clear();
            _contentTypeCache.Clear();
            return;
        }

        _crcCache.TryRemove(profileId, out _);
        _contentTypeCache.Clear();
    }

    private static GeneralsOnlineCompatibility Evaluate(ProfileCrcs profile, uint lobbyExeCrc, uint lobbyIniCrc)
    {
        if (profile.ExeCrc is null || profile.IniCrc is null)
        {
            return GeneralsOnlineCompatibility.Unknown;
        }

        if (lobbyExeCrc == 0 || lobbyIniCrc == 0)
        {
            return GeneralsOnlineCompatibility.Unknown;
        }

        if (profile.ExeCrc.Value != lobbyExeCrc)
        {
            return GeneralsOnlineCompatibility.ExeMismatch;
        }

        return profile.IniCrc.Value == lobbyIniCrc
            ? GeneralsOnlineCompatibility.Compatible
            : GeneralsOnlineCompatibility.IniMismatch;
    }

    private static int RankOf(GeneralsOnlineCompatibility compatibility) => compatibility switch
    {
        GeneralsOnlineCompatibility.Compatible => 0,
        GeneralsOnlineCompatibility.IniMismatch => 1,
        GeneralsOnlineCompatibility.ExeMismatch => 2,
        _ => 3,
    };

    private static uint? ParseCrc(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        var text = hex.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        return uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string FormatCrc(uint? crc) => crc.HasValue ? FormatLobbyCrc(crc.Value) : "null";

    private static string FormatLobbyCrc(uint crc) => $"0x{crc:X8}";

    private static string? ResolveInstallationPath(GameInstallation installation, GameType? gameType)
    {
        var specific = gameType == GameType.Generals ? installation.GeneralsPath : installation.ZeroHourPath;
        if (!string.IsNullOrWhiteSpace(specific) && Directory.Exists(specific))
        {
            return specific;
        }

        return !string.IsNullOrWhiteSpace(installation.InstallationPath) && Directory.Exists(installation.InstallationPath)
            ? installation.InstallationPath
            : null;
    }

    private static string? ResolveClientRoot(GameProfile profile, string? fullExe)
    {
        if (!string.IsNullOrEmpty(fullExe) && Path.IsPathRooted(fullExe))
        {
            var exeDir = Path.GetDirectoryName(fullExe);
            if (!string.IsNullOrEmpty(exeDir) && Directory.Exists(exeDir))
            {
                return exeDir;
            }
        }

        if (!string.IsNullOrWhiteSpace(profile.WorkingDirectory) && Directory.Exists(profile.WorkingDirectory))
        {
            return profile.WorkingDirectory;
        }

        var clientDir = profile.GameClient?.WorkingDirectory;
        if (!string.IsNullOrWhiteSpace(clientDir) && Directory.Exists(clientDir))
        {
            return clientDir;
        }

        return null;
    }

    private static string? ResolveExeCandidate(GameProfile profile, string gameRoot)
    {
        var candidates = new List<string?>();
        if (!string.IsNullOrWhiteSpace(profile.CustomExecutablePath))
        {
            candidates.Add(RootOrCombine(gameRoot, profile.CustomExecutablePath));
        }

        if (!string.IsNullOrWhiteSpace(profile.ExecutablePath))
        {
            candidates.Add(RootOrCombine(gameRoot, profile.ExecutablePath));
        }

        if (profile.GameClient is not null)
        {
            if (!string.IsNullOrWhiteSpace(profile.GameClient.ExecutablePath))
            {
                candidates.Add(RootOrCombine(gameRoot, profile.GameClient.ExecutablePath));
            }

            var defaultName = ReplayCrcMatchingHelper.GetDefaultExecutableName(
                profile.GameClient.GameType,
                profile.GameClient.PublisherType);
            candidates.Add(Path.Combine(gameRoot, defaultName));
        }

        candidates.Add(Path.Combine(gameRoot, GameClientConstants.ZeroHourExecutable));
        candidates.Add(Path.Combine(gameRoot, GameClientConstants.GeneralsExecutable));

        return candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c) && File.Exists(c));
    }

    private static string RootOrCombine(string gameRoot, string path)
    {
        return Path.IsPathRooted(path) ? path : Path.Combine(gameRoot, path);
    }

    private static string? ResolveClientDirectory(string? clientExe)
    {
        if (string.IsNullOrEmpty(clientExe))
        {
            return null;
        }

        var clientDir = Path.GetDirectoryName(clientExe);
        return !string.IsNullOrEmpty(clientDir) && Directory.Exists(clientDir)
            ? clientDir
            : null;
    }

    private async Task<ProfileCrcs> ResolveProfileCrcsAsync(GameProfile profile, CancellationToken cancellationToken)
    {
        if (TryGetFreshCrcs(profile.Id, out var cached))
        {
            return cached;
        }

        await _computeLock.WaitAsync(cancellationToken);
        try
        {
            if (TryGetFreshCrcs(profile.Id, out cached))
            {
                return cached;
            }

            var computed = await ComputeProfileCrcsAsync(profile, cancellationToken);
            _crcCache[profile.Id] = (computed, _timeProvider.GetUtcNow().UtcDateTime);
            return computed;
        }
        finally
        {
            _computeLock.Release();
        }
    }

    private bool TryGetFreshCrcs(string profileId, [NotNullWhen(true)] out ProfileCrcs? crcs)
    {
        crcs = null;
        if (!_crcCache.TryGetValue(profileId, out var cached))
        {
            return false;
        }

        if (_timeProvider.GetUtcNow().UtcDateTime - cached.CachedAtUtc > TimeSpan.FromMinutes(OnlineConstants.ProfileSetupCacheTtlMinutes))
        {
            // Remove only the observed stale value: a concurrent recompute may
            // have already stored a fresh entry for this profile.
            var staleEntry = KeyValuePair.Create(profileId, cached);
            ((ICollection<KeyValuePair<string, (ProfileCrcs Crcs, DateTime CachedAtUtc)>>)_crcCache).Remove(staleEntry);
            return false;
        }

        crcs = cached.Crcs;
        return true;
    }

    private async Task<ProfileCrcs> ComputeProfileCrcsAsync(GameProfile profile, CancellationToken cancellationToken)
    {
        try
        {
            if (crcCalculator is null || profile.GameClient is null)
            {
                return new ProfileCrcs(null, null);
            }

            var (gameRoot, exePath) = await ResolveGamePathsAsync(profile, cancellationToken);
            if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot))
            {
                return new ProfileCrcs(null, null);
            }

            uint? exeCrc = null;
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                // Lobbies report the engine value, so profiles must use the engine-exact
                // calculation rather than the legacy replay-oriented one.
                var exeResult = await crcCalculator.CalculateEngineExeCrcAsync(exePath, profile.GameClient.GameType, gameRoot, ct: cancellationToken);
                if (exeResult.Success)
                {
                    exeCrc = ParseCrc(exeResult.Data);
                }
            }

            var contentTypes = await GetContentTypeMapAsync(profile, cancellationToken);
            var gameplayIds = OnlineProfileMatcher.GetGameplayContentIds(profile, contentTypes);
            var sideloads = await ResolveGameplaySideloadsAsync(profile, gameplayIds, cancellationToken);
            var iniResult = await crcCalculator.CalculateIniCrcAsync(
                gameRoot,
                profile.GameClient.GameType,
                sideloads,
                null,
                ct: cancellationToken);
            var iniCrc = iniResult.Success ? ParseCrc(iniResult.Data) : null;
            logger.LogDebug(
                "Generals Online CRCs for profile {ProfileId}: root {GameRoot}, exe {ExePath}, sideloads {SideloadCount}, exeCrc {ExeCrc}, iniCrc {IniCrc}.",
                profile.Id,
                gameRoot,
                exePath,
                sideloads.Count,
                FormatCrc(exeCrc),
                FormatCrc(iniCrc));
            return new ProfileCrcs(exeCrc, iniCrc);
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "Profile {ProfileId} CRCs unavailable; compatibility stays unknown.", profile.Id);
            return new ProfileCrcs(null, null);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogDebug(ex, "Profile {ProfileId} CRCs unavailable; compatibility stays unknown.", profile.Id);
            return new ProfileCrcs(null, null);
        }
        catch (ArgumentException ex)
        {
            logger.LogDebug(ex, "Profile {ProfileId} CRCs unavailable; compatibility stays unknown.", profile.Id);
            return new ProfileCrcs(null, null);
        }
    }

    private async Task<(string? GameRoot, string? ExePath)> ResolveGamePathsAsync(
        GameProfile profile,
        CancellationToken cancellationToken)
    {
        string? gameRoot = null;
        if (installationService is not null && !string.IsNullOrWhiteSpace(profile.GameInstallationId))
        {
            gameRoot = await ResolveConfiguredRootAsync(profile, cancellationToken);
        }

        // The configured root wins for both hashes. Falling back to the
        // client-recorded executable here would mix an exe CRC from the old
        // location with INI CRCs from the configured root.
        var fullExe = ReplayCrcMatchingHelper.ResolveProfileFullExePath(profile.GameClient);
        string? exePath = null;
        if (string.IsNullOrEmpty(gameRoot))
        {
            gameRoot = ResolveClientRoot(profile, fullExe);
            if (!string.IsNullOrEmpty(fullExe) && File.Exists(fullExe))
            {
                exePath = fullExe;
            }
        }

        if (!string.IsNullOrEmpty(gameRoot))
        {
            exePath ??= ResolveExeCandidate(profile, gameRoot);
        }

        return (gameRoot, exePath);
    }

    private async Task<string?> ResolveConfiguredRootAsync(GameProfile profile, CancellationToken cancellationToken)
    {
        try
        {
            var install = await installationService!.GetInstallationAsync(profile.GameInstallationId!, cancellationToken);
            if (install.Success && install.Data is not null)
            {
                return ResolveInstallationPath(install.Data, profile.GameClient?.GameType);
            }
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "Failed to resolve installation root for profile {ProfileId}.", profile.Id);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogDebug(ex, "Failed to resolve installation root for profile {ProfileId}.", profile.Id);
        }
        catch (ArgumentException ex)
        {
            logger.LogDebug(ex, "Failed to resolve installation root for profile {ProfileId}.", profile.Id);
        }

        return null;
    }

    private async Task<IReadOnlyList<string>> ResolveGameplaySideloadsAsync(
        GameProfile profile,
        IReadOnlyList<string> gameplayIds,
        CancellationToken cancellationToken)
    {
        var client = profile.GameClient;
        if (client is null)
        {
            return [];
        }

        var sideloads = new List<string>();
        var clientDir = ResolveClientDirectory(ReplayCrcMatchingHelper.ResolveProfileFullExePath(client));
        if (clientDir is not null)
        {
            sideloads.Add(clientDir);
        }

        if (gameplayIds.Count > 0)
        {
            var available = await profileManager.GetAvailableContentAsync(client, cancellationToken);
            if (available.Success && available.Data is not null)
            {
                var wanted = new HashSet<string>(gameplayIds, StringComparer.Ordinal);
                sideloads.AddRange(available.Data
                    .Where(manifest => wanted.Contains(manifest.Id.ToString()))
                    .Select(manifest => manifest.SourcePath)
                    .OfType<string>()
                    .Where(path => !string.IsNullOrWhiteSpace(path)));
            }
        }

        return sideloads
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<IReadOnlyDictionary<string, ContentType>?> GetContentTypeMapAsync(
        GameProfile profile,
        CancellationToken cancellationToken)
    {
        var client = profile.GameClient;
        if (client is null)
        {
            return null;
        }

        var cacheKey = OnlineProfileMatcher.GetGameClientKey(profile);
        if (_contentTypeCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var dict = new Dictionary<string, ContentType>(StringComparer.Ordinal);
        if (manifestPool is not null)
        {
            var allManifests = await manifestPool.GetAllManifestsAsync(cancellationToken);
            if (allManifests.Success && allManifests.Data is not null)
            {
                foreach (var manifest in allManifests.Data)
                {
                    dict[manifest.Id.ToString()] = manifest.ContentType;
                }
            }
        }

        var available = await profileManager.GetAvailableContentAsync(client, cancellationToken);
        if (available.Success && available.Data is not null)
        {
            foreach (var group in available.Data.GroupBy(m => m.Id.ToString(), StringComparer.Ordinal))
            {
                dict[group.Key] = OnlineProfileMatcher.ResolveDeclaredType(group.Select(m => m.ContentType));
            }
        }

        _contentTypeCache[cacheKey] = dict;
        return dict;
    }
}
