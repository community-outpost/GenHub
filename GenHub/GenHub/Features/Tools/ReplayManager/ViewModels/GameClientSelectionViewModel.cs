using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Tools.Checksum;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Tools.ReplayManager;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ReplayManager.ViewModels;

/// <summary>
/// ViewModel for selecting a compatible game client to create a dedicated profile for a replay.
/// Surfaces CRC-compatible clients first and allows toggling to view all available catalog/profile clients.
/// </summary>
public sealed partial class GameClientSelectionViewModel(
    IGameProfileManager profileManager,
    IContentManifestPool manifestPool,
    ICrcMappingRegistry crcMappingRegistry,
    ILogger<GameClientSelectionViewModel> logger,
    IGameCrcCalculatorService? crcCalculator = null,
    IGameInstallationService? installationService = null) : ObservableObject
{
    private const string CrcCompatibleCategory = "CRC Compatible";

    private readonly List<GameClientCardViewModel> _allClients = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<GameClientCardViewModel> _filteredClients = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _replayFileName = string.Empty;

    [ObservableProperty]
    private string _replayExeCrc = string.Empty;

    [ObservableProperty]
    private string _replayIniCrc = string.Empty;

    [ObservableProperty]
    private bool _hasCrcInfo;

    [ObservableProperty]
    private bool _showAllClients;

    [ObservableProperty]
    private bool _hasCompatibleCrcClients;

    [ObservableProperty]
    private int _compatibleCount;

    [ObservableProperty]
    private GameType _targetGame;

    [ObservableProperty]
    private GameClient? _selectedClient;

    [ObservableProperty]
    private string? _selectedManifestId;

    [ObservableProperty]
    private bool _wasSuccessful;

    /// <summary>
    /// Event raised when the dialog window should be closed.
    /// </summary>
    public event EventHandler? RequestClose;

    /// <summary>
    /// Loads available game clients for the specified game type and replay.
    /// </summary>
    /// <param name="targetGame">The game type (Generals or Zero Hour).</param>
    /// <param name="replayFileName">The name of the replay file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task LoadClientsAsync(GameType targetGame, string replayFileName, CancellationToken ct = default)
    {
        TargetGame = targetGame;
        ReplayFileName = replayFileName;
        IsLoading = true;
        _allClients.Clear();
        FilteredClients.Clear();

        try
        {
            logger.LogInformation(
                "[ReplayManager] Discovering available game clients for replay '{ReplayName}' (TargetGame: {TargetGame})",
                replayFileName,
                targetGame);

            var discoveredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Step 1: Discover catalog manifests
            await DiscoverManifestClientsAsync(targetGame, null, discoveredKeys, ct);

            // Step 2: Discover existing profile clients
            await DiscoverProfileClientsAsync(targetGame, null, discoveredKeys, ct);

            // Step 3: Discover base installations
            await DiscoverInstallationClientsAsync(targetGame, discoveredKeys, ct);

            // Step 4: Always add retail client card as fallback
            AddRetailClientCard(targetGame, null, discoveredKeys);

            UpdateCompatibilityCounts();
            ApplyFilter();

            logger.LogInformation(
                "[ReplayManager] Discovered {TotalCount} clients for {TargetGame}",
                _allClients.Count,
                targetGame);
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ReplayManager] Failed to discover game clients for replay '{ReplayName}'", replayFileName);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Backward-compatible overload for loading clients with an optional replay file.
    /// </summary>
    /// <param name="targetGame">The game type (Generals or Zero Hour).</param>
    /// <param name="replayFileName">The name of the replay file.</param>
    /// <param name="replay">The replay file with parsed CRC metadata.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task LoadClientsAsync(
        GameType targetGame,
        string replayFileName,
        ReplayFile? replay,
        CancellationToken ct = default)
        => LoadClientsForReplayAsync(targetGame, replay, ct);

    /// <summary>
    /// Loads available game clients for the specified replay with CRC-aware matching.
    /// </summary>
    /// <param name="targetGame">The game type (Generals or Zero Hour).</param>
    /// <param name="replay">The replay file with parsed CRC metadata.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task LoadClientsForReplayAsync(GameType targetGame, ReplayFile? replay, CancellationToken ct = default)
    {
        TargetGame = targetGame;
        ReplayFileName = replay?.FileName ?? string.Empty;
        IsLoading = true;
        _allClients.Clear();
        FilteredClients.Clear();

        InitializeCrcInfo(replay);

        try
        {
            logger.LogInformation(
                "[ReplayManager] Discovering game clients for replay '{ReplayName}' (Game: {TargetGame}, EXE CRC: {ExeCrc}, INI CRC: {IniCrc})",
                ReplayFileName,
                targetGame,
                ReplayExeCrc,
                ReplayIniCrc);

            var matchedClient = ResolveMatchedClient(replay);
            var discoveredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Step 1: If CRC matched a known client, add it first as top-priority card
            if (matchedClient != null)
            {
                AddMatchedClientCard(matchedClient, targetGame, discoveredKeys);
            }

            // Step 2: Discover catalog manifests
            await DiscoverManifestClientsAsync(targetGame, matchedClient, discoveredKeys, ct);

            // Step 3: Discover existing profile clients
            await DiscoverProfileClientsAsync(targetGame, matchedClient, discoveredKeys, ct);

            // Step 4: Discover base installations
            await DiscoverInstallationClientsAsync(targetGame, discoveredKeys, ct);

            // Step 5: Always add retail client card as fallback
            AddRetailClientCard(targetGame, replay, discoveredKeys);

            UpdateCompatibilityCounts();
            ApplyFilter();

            logger.LogInformation(
                "[ReplayManager] Discovered {TotalCount} clients for {TargetGame}",
                _allClients.Count,
                targetGame);
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ReplayManager] Failed to discover game clients for replay '{ReplayName}'", ReplayFileName);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Determines whether a manifest matches the target game type.
    /// Falls back to token matching in the manifest ID if TargetGame is Unknown.
    /// </summary>
    /// <param name="manifest">The content manifest.</param>
    /// <param name="targetGame">The target game.</param>
    /// <returns><c>true</c> if the manifest matches the target game; otherwise, <c>false</c>.</returns>
    internal static bool ManifestMatchesGame(ContentManifest manifest, GameType targetGame)
    {
        if (manifest.TargetGame == targetGame)
        {
            return true;
        }

        if (manifest.TargetGame != GameType.Unknown)
        {
            return false;
        }

        var tokens = (manifest.Id.Value ?? string.Empty).ToLowerInvariant().Split('-', '_', '.');

        if (targetGame == GameType.ZeroHour)
        {
            return tokens.Any(t => string.Equals(t, "zh", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(t, "zerohour", StringComparison.OrdinalIgnoreCase));
        }

        if (targetGame == GameType.Generals)
        {
            return tokens.Any(t => string.Equals(t, "generals", StringComparison.OrdinalIgnoreCase) ||
                                   t.StartsWith("generals-", StringComparison.OrdinalIgnoreCase) ||
                                   t.EndsWith("-generals", StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    private static string GetPublisherDisplayName(PublisherInfo? publisher)
    {
        if (!string.IsNullOrWhiteSpace(publisher?.Name))
        {
            return publisher.Name;
        }

        if (!string.IsNullOrWhiteSpace(publisher?.PublisherType))
        {
            return publisher.PublisherType;
        }

        return "Catalog";
    }

    private static bool IsMatchedByVersion(CrcMappingEntry? matchedClient, GameClient client)
    {
        if (matchedClient != null && !string.IsNullOrEmpty(matchedClient.Version) && !string.IsNullOrEmpty(client.Version))
        {
            return string.Equals(matchedClient.Version.TrimStart('0'), client.Version.TrimStart('0'), StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    [RelayCommand]
    private void ToggleShowAll()
    {
        ShowAllClients = !ShowAllClients;
        ApplyFilter();
    }

    [RelayCommand]
    private void Cancel()
    {
        WasSuccessful = false;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private void InitializeCrcInfo(ReplayFile? replay)
    {
        if (replay?.ExeCrc is { } exeCrc && exeCrc != 0)
        {
            ReplayExeCrc = replay.Metadata?.FormattedExeCrc ?? $"0x{exeCrc:X8}";
            ReplayIniCrc = replay.Metadata?.FormattedIniCrc ?? (replay.IniCrc.HasValue ? $"0x{replay.IniCrc.Value:X8}" : string.Empty);
            HasCrcInfo = true;
        }
        else
        {
            ReplayExeCrc = string.Empty;
            ReplayIniCrc = string.Empty;
            HasCrcInfo = false;
        }

        logger.LogDebug(
            "[ReplayManager] Initialized CRC info: Exe={ExeCrc}, Ini={IniCrc}, HasInfo={HasInfo}",
            ReplayExeCrc,
            ReplayIniCrc,
            HasCrcInfo);
    }

    private CrcMappingEntry? ResolveMatchedClient(ReplayFile? replay)
    {
        if (replay?.MatchedClient != null)
        {
            return replay.MatchedClient;
        }

        if (string.IsNullOrEmpty(ReplayExeCrc))
        {
            return null;
        }

        if (!string.IsNullOrEmpty(ReplayIniCrc) && crcMappingRegistry.TryGetEntry(ReplayExeCrc, ReplayIniCrc, out var entry))
        {
            return entry;
        }

        return crcMappingRegistry.TryGetEntryByExeCrc(ReplayExeCrc, out var exeEntry) ? exeEntry : null;
    }

    private void AddMatchedClientCard(CrcMappingEntry matchedClient, GameType targetGame, ISet<string> discoveredKeys)
    {
        var matchedClientModel = new GameClient
        {
            Id = matchedClient.ManifestId,
            Name = matchedClient.Description ?? $"{matchedClient.Publisher} {matchedClient.Version}",
            Version = matchedClient.Version ?? string.Empty,
            PublisherType = matchedClient.Publisher ?? string.Empty,
            GameType = targetGame,
        };

        var card = new GameClientCardViewModel(new GameClientCardParameters(
            Client: matchedClientModel,
            ManifestId: matchedClient.ManifestId,
            Name: matchedClient.Description ?? $"{matchedClient.Publisher} {matchedClient.Version}",
            Version: matchedClient.Version ?? "Unknown",
            Publisher: matchedClient.Publisher ?? "Unknown",
            Category: CrcCompatibleCategory,
            ExecutablePath: string.Empty,
            Description: $"Exact match for replay CRC (EXE: {ReplayExeCrc}, INI: {ReplayIniCrc})",
            OnSelect: OnClientSelected,
            IsCrcMatch: true));

        _allClients.Add(card);
        if (!string.IsNullOrEmpty(matchedClient.ManifestId))
        {
            discoveredKeys.Add(matchedClient.ManifestId);
        }
        if (matchedClient.ManifestId.Contains("retail", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(matchedClient.Publisher, "retail", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(matchedClient.Publisher, "ea", StringComparison.OrdinalIgnoreCase))
        {
            discoveredKeys.Add("retail-base-client");
        }
    }

    private void AddRetailClientCard(GameType targetGame, ReplayFile? replay, ISet<string> discoveredKeys)
    {
        if (discoveredKeys.Contains("retail-base-client") ||
            _allClients.Any(c => c.IsCrcMatch && (c.Publisher.Contains("Retail", StringComparison.OrdinalIgnoreCase) ||
                                                  c.Publisher.Contains("EA", StringComparison.OrdinalIgnoreCase) ||
                                                  c.Name.Contains("Retail", StringComparison.OrdinalIgnoreCase))))
        {
            return;
        }

        var exeCrc = replay?.ExeCrc ?? 0;
        var isRetailZeroHour = targetGame == GameType.ZeroHour &&
            (exeCrc == 0xDA2B4B18 || exeCrc == 0x401D89EA || exeCrc == 0x887B0CAA ||
             string.Equals(ReplayExeCrc, "0xDA2B4B18", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(ReplayExeCrc, "0x401D89EA", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(ReplayExeCrc, "0x887B0CAA", StringComparison.OrdinalIgnoreCase));

        var isRetailGenerals = targetGame == GameType.Generals &&
            (exeCrc == 0x1C96366F || exeCrc == 0x27533BB0 ||
             string.Equals(ReplayExeCrc, "0x1C96366F", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(ReplayExeCrc, "0x27533BB0", StringComparison.OrdinalIgnoreCase));

        var isRetailMatch = isRetailZeroHour || isRetailGenerals;

        var retailName = targetGame == GameType.ZeroHour ? "Retail 1.04" : "Retail 1.0";
        var retailClient = new GameClient
        {
            Id = string.Empty,
            Name = retailName,
            Version = targetGame == GameType.ZeroHour ? ManifestConstants.ZeroHourManifestVersion : ManifestConstants.GeneralsManifestVersion,
            PublisherType = "Retail",
            GameType = targetGame,
        };

        var retailDescription = isRetailMatch
            ? $"Exact match for retail replay CRC (EXE: {ReplayExeCrc})"
            : "Standard retail executable using base game installation";

        var retailCard = new GameClientCardViewModel(new GameClientCardParameters(
            Client: retailClient,
            ManifestId: string.Empty,
            Name: retailName,
            Version: retailClient.Version,
            Publisher: "EA / Retail",
            Category: isRetailMatch ? CrcCompatibleCategory : "Retail Fallback",
            ExecutablePath: string.Empty,
            Description: retailDescription,
            OnSelect: OnClientSelected,
            IsCrcMatch: isRetailMatch));

        if (discoveredKeys.Add(retailName))
        {
            _allClients.Add(retailCard);
            if (isRetailMatch)
            {
                discoveredKeys.Add("retail-base-client");
            }
        }
    }

    private void UpdateCompatibilityCounts()
    {
        CompatibleCount = _allClients.Count(c => c.IsCrcMatch);
        HasCompatibleCrcClients = CompatibleCount > 0;
        ShowAllClients = false;
    }

    private async Task DiscoverManifestClientsAsync(
        GameType targetGame,
        CrcMappingEntry? matchedClient,
        HashSet<string> discoveredKeys,
        CancellationToken ct)
    {
        try
        {
            var manifestsResult = await manifestPool.GetAllManifestsAsync(ct);
            if (!manifestsResult.Success || manifestsResult.Data == null)
            {
                return;
            }

            foreach (var manifest in manifestsResult.Data.Where(m => m.ContentType == ContentType.GameClient))
            {
                if (!ManifestMatchesGame(manifest, targetGame))
                {
                    continue;
                }

                var dedupeKey = $"{manifest.Name}|{manifest.Id.Value}";
                if (!discoveredKeys.Add(dedupeKey))
                {
                    continue;
                }

                var isCrcMatch = await IsManifestCrcMatchAsync(manifest, matchedClient, ct);
                _allClients.Add(CreateManifestGameClientCard(manifest, targetGame, isCrcMatch));
                if (isCrcMatch && (manifest.Id.Value.Contains("retail", StringComparison.OrdinalIgnoreCase) ||
                                   manifest.Publisher?.PublisherType?.Contains("retail", StringComparison.OrdinalIgnoreCase) == true))
                {
                    discoveredKeys.Add("retail-base-client");
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[ReplayManager] Error querying manifests for game clients");
        }
    }

    private async Task<bool> IsManifestCrcMatchAsync(
        ContentManifest manifest,
        CrcMappingEntry? matchedClient,
        CancellationToken ct)
    {
        if (matchedClient != null)
        {
            var idMatch = string.Equals(manifest.Id.Value, matchedClient.ManifestId, StringComparison.OrdinalIgnoreCase);
            var versionMatch = !string.IsNullOrEmpty(matchedClient.Version) &&
                               !string.IsNullOrEmpty(manifest.Version) &&
                               string.Equals(matchedClient.Version.TrimStart('0'), manifest.Version.TrimStart('0'), StringComparison.OrdinalIgnoreCase);

            if (idMatch || (versionMatch && string.Equals(manifest.Publisher?.PublisherType, matchedClient.Publisher, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        if (!string.IsNullOrEmpty(ReplayExeCrc) && crcMappingRegistry != null)
        {
            var allEntries = crcMappingRegistry.GetAllEntries();
            if (allEntries.Any(e => string.Equals(e.ExeCrc, ReplayExeCrc, StringComparison.OrdinalIgnoreCase) &&
                                    (string.Equals(e.ManifestId, manifest.Id.Value, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(e.DataPatchManifestId, manifest.Id.Value, StringComparison.OrdinalIgnoreCase))))
            {
                return true;
            }
        }

        var isZh104Exe = string.Equals(ReplayExeCrc, "0xDA2B4B18", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(ReplayExeCrc, "0x401D89EA", StringComparison.OrdinalIgnoreCase);
        if (isZh104Exe && manifest.TargetGame == GameType.ZeroHour)
        {
            var pub = manifest.Publisher?.PublisherType ?? string.Empty;
            if (pub.Contains("community", StringComparison.OrdinalIgnoreCase) ||
                pub.Contains("outpost", StringComparison.OrdinalIgnoreCase) ||
                pub.Contains("retail", StringComparison.OrdinalIgnoreCase) ||
                pub.Contains("ea", StringComparison.OrdinalIgnoreCase) ||
                manifest.Id.Value.Contains("community-patch", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (manifestPool != null && !string.IsNullOrEmpty(ReplayExeCrc))
        {
            try
            {
                var contentDirResult = await manifestPool.GetContentDirectoryAsync(manifest.Id, ct);
                if (contentDirResult.Success && !string.IsNullOrEmpty(contentDirResult.Data) && Directory.Exists(contentDirResult.Data))
                {
                    var exeFiles = Directory.GetFiles(contentDirResult.Data, "*.exe", SearchOption.AllDirectories);
                    foreach (var exeFile in exeFiles)
                    {
                        if (await CheckExeCrcMatchAsync(exeFile, null, ct))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "[ReplayManager] Error checking manifest content directory for CRC match");
            }
        }

        return false;
    }

    private GameClientCardViewModel CreateManifestGameClientCard(
        ContentManifest manifest,
        GameType targetGame,
        bool isCrcMatch)
    {
        var client = new GameClient
        {
            Id = manifest.Id.Value,
            Name = manifest.Name,
            Version = manifest.Version ?? "1.0",
            PublisherType = manifest.Publisher?.PublisherType ?? "Custom",
            GameType = targetGame,
        };

        var displayName = !string.IsNullOrWhiteSpace(manifest.Name)
            ? manifest.Name
            : $"Client {manifest.Id.Value}";

        var publisherName = GetPublisherDisplayName(manifest.Publisher);

        var description = !string.IsNullOrWhiteSpace(manifest.Metadata?.Description)
            ? manifest.Metadata.Description
            : $"Catalog manifest {manifest.Id.Value}";

        var category = isCrcMatch ? CrcCompatibleCategory : "Catalog Manifest";

        return new GameClientCardViewModel(new GameClientCardParameters(
            Client: client,
            ManifestId: manifest.Id.Value,
            Name: displayName,
            Version: manifest.Version ?? "1.0",
            Publisher: publisherName,
            Category: category,
            ExecutablePath: string.Empty,
            Description: description,
            OnSelect: OnClientSelected,
            IsCrcMatch: isCrcMatch));
    }

    private async Task DiscoverProfileClientsAsync(
        GameType targetGame,
        CrcMappingEntry? matchedClient,
        HashSet<string> discoveredKeys,
        CancellationToken ct)
    {
        try
        {
            var profilesResult = await profileManager.GetAllProfilesAsync(ct);
            if (!profilesResult.Success || profilesResult.Data == null)
            {
                return;
            }

            foreach (var profile in profilesResult.Data.Where(p => p.GameClient?.GameType == targetGame))
            {
                await ProcessProfileClientAsync(profile, matchedClient, discoveredKeys, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[ReplayManager] Error querying profiles for game clients");
        }
    }

    private async Task ProcessProfileClientAsync(
        GameProfile profile,
        CrcMappingEntry? matchedClient,
        HashSet<string> discoveredKeys,
        CancellationToken ct)
    {
        var client = profile.GameClient;
        if (client == null)
        {
            return;
        }

        var clientName = !string.IsNullOrWhiteSpace(client.Name)
            ? client.Name
            : profile.Name;

        var exePath = client.ExecutablePath ?? string.Empty;
        var dedupeKey = $"{clientName}|{exePath}";

        if (!discoveredKeys.Add(dedupeKey))
        {
            return;
        }

        var isCrcMatch = IsMatchedByVersion(matchedClient, client);
        if (!isCrcMatch)
        {
            isCrcMatch = await CheckExeCrcMatchAsync(exePath, client.WorkingDirectory, ct);
        }
        if (!isCrcMatch && !string.IsNullOrEmpty(matchedClient?.ManifestId) &&
            string.Equals(client.Id, matchedClient.ManifestId, StringComparison.OrdinalIgnoreCase))
        {
            isCrcMatch = true;
        }
        if (!isCrcMatch && (string.Equals(ReplayExeCrc, "0xDA2B4B18", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(ReplayExeCrc, "0x401D89EA", StringComparison.OrdinalIgnoreCase)) &&
            client.GameType == GameType.ZeroHour)
        {
            var pub = client.PublisherType ?? string.Empty;
            if (pub.Contains("community", StringComparison.OrdinalIgnoreCase) ||
                pub.Contains("retail", StringComparison.OrdinalIgnoreCase) ||
                pub.Contains("ea", StringComparison.OrdinalIgnoreCase) ||
                pub.Contains("steam", StringComparison.OrdinalIgnoreCase))
            {
                isCrcMatch = true;
            }
        }
        if (isCrcMatch && (client.PublisherType?.Contains("retail", StringComparison.OrdinalIgnoreCase) == true ||
                           client.Name.Contains("retail", StringComparison.OrdinalIgnoreCase)))
        {
            discoveredKeys.Add("retail-base-client");
        }

        var description = isCrcMatch
            ? $"CRC match (EXE: {ReplayExeCrc}) from existing profile '{profile.Name}'"
            : $"Configured in existing profile '{profile.Name}'";

        _allClients.Add(new GameClientCardViewModel(new GameClientCardParameters(
            Client: client,
            ManifestId: client.Id,
            Name: clientName,
            Version: client.Version ?? "Custom",
            Publisher: client.PublisherType ?? "Local Profile",
            Category: isCrcMatch ? CrcCompatibleCategory : "Local Profile",
            ExecutablePath: exePath,
            Description: description,
            OnSelect: OnClientSelected,
            IsCrcMatch: isCrcMatch)));
    }

    private async Task DiscoverInstallationClientsAsync(
        GameType targetGame,
        HashSet<string> discoveredKeys,
        CancellationToken ct)
    {
        if (installationService == null)
        {
            return;
        }

        try
        {
            var installationsResult = await installationService.GetAllInstallationsAsync(ct);
            if (!installationsResult.Success || installationsResult.Data == null)
            {
                return;
            }

            foreach (var installation in installationsResult.Data)
            {
                if (installation.AvailableGameClients == null)
                {
                    continue;
                }

                foreach (var client in installation.AvailableGameClients.Where(c => c.GameType == targetGame))
                {
                    await ProcessInstallationClientAsync(installation, client, discoveredKeys, ct);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[ReplayManager] Error querying game installations for clients");
        }
    }

    private async Task ProcessInstallationClientAsync(
        GameInstallation installation,
        GameClient client,
        HashSet<string> discoveredKeys,
        CancellationToken ct)
    {
        var clientName = !string.IsNullOrWhiteSpace(client.Name)
            ? client.Name
            : $"{installation.InstallationType} Client";

        var exePath = client.ExecutablePath ?? string.Empty;
        var dedupeKey = $"{clientName}|{exePath}";

        if (!discoveredKeys.Add(dedupeKey))
        {
            return;
        }

        var fullExePath = exePath;
        if (!Path.IsPathRooted(fullExePath) && !string.IsNullOrWhiteSpace(client.WorkingDirectory))
        {
            fullExePath = Path.Combine(client.WorkingDirectory, fullExePath);
        }

        if (string.IsNullOrEmpty(exePath) || Directory.Exists(fullExePath))
        {
            var basePath = !string.IsNullOrEmpty(fullExePath) && Directory.Exists(fullExePath)
                ? fullExePath
                : (!string.IsNullOrWhiteSpace(client.WorkingDirectory)
                    ? client.WorkingDirectory
                    : (installation.InstallationPath ?? string.Empty));

            if (!string.IsNullOrEmpty(basePath) && Directory.Exists(basePath))
            {
                var candidateExe = Path.Combine(basePath, "generals.exe");
                if (File.Exists(candidateExe))
                {
                    fullExePath = candidateExe;
                    exePath = candidateExe;
                }
            }
        }

        var isCrcMatch = await CheckExeCrcMatchAsync(fullExePath, client.WorkingDirectory, ct);
        if (!isCrcMatch && (string.Equals(ReplayExeCrc, "0xDA2B4B18", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(ReplayExeCrc, "0x401D89EA", StringComparison.OrdinalIgnoreCase)) &&
            client.GameType == GameType.ZeroHour)
        {
            var name = clientName.ToLowerInvariant();
            if (name.Contains("community") || name.Contains("patch") || name.Contains("retail") || name.Contains("steam"))
            {
                isCrcMatch = true;
            }
        }
        if (isCrcMatch && (installation.InstallationType == InstallationType.Retail ||
                           installation.InstallationType == InstallationType.Steam ||
                           installation.InstallationType == InstallationType.EaApp))
        {
            discoveredKeys.Add("retail-base-client");
        }

        var description = isCrcMatch
            ? $"CRC match (EXE: {ReplayExeCrc}) from {installation.InstallationType} installation"
            : $"Client from {installation.InstallationType} installation";

        _allClients.Add(new GameClientCardViewModel(new GameClientCardParameters(
            Client: client,
            ManifestId: client.Id,
            Name: clientName,
            Version: client.Version ?? "Base",
            Publisher: client.PublisherType ?? $"{installation.InstallationType}",
            Category: isCrcMatch ? CrcCompatibleCategory : "Base Installation",
            ExecutablePath: exePath,
            Description: description,
            OnSelect: OnClientSelected,
            IsCrcMatch: isCrcMatch)));
    }

    private async Task<bool> CheckExeCrcMatchAsync(string exePath, string? workingDirectory, CancellationToken ct)
    {
        if (crcCalculator == null || string.IsNullOrEmpty(exePath) || string.IsNullOrEmpty(ReplayExeCrc))
        {
            return false;
        }

        var fullExePath = exePath;
        if (!Path.IsPathRooted(fullExePath) && !string.IsNullOrWhiteSpace(workingDirectory))
        {
            fullExePath = Path.Combine(workingDirectory, fullExePath);
        }

        if (!File.Exists(fullExePath))
        {
            return false;
        }

        try
        {
            var calcRes = await crcCalculator.CalculateExeCrcAsync(fullExePath, ct: ct);
            if (calcRes.Success && string.Equals(calcRes.Data, ReplayExeCrc, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "[ReplayManager] Could not calculate CRC for executable: {ExePath}", fullExePath);
        }

        return false;
    }

    private void ApplyFilter()
    {
        var query = SearchText?.Trim();
        IEnumerable<GameClientCardViewModel> candidates = _allClients;

        if (!ShowAllClients)
        {
            candidates = HasCompatibleCrcClients
                ? candidates.Where(c => c.IsCrcMatch)
                : Enumerable.Empty<GameClientCardViewModel>();
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            candidates = candidates.Where(c =>
                c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Version.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Description.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var sorted = candidates
            .OrderByDescending(c => c.IsCrcMatch)
            .ThenBy(c => c.Name)
            .ToList();

        FilteredClients = new ObservableCollection<GameClientCardViewModel>(sorted);
    }

    private void OnClientSelected(GameClientCardViewModel card)
    {
        logger.LogInformation(
            "[ReplayManager] Selected client '{Name}' (Version: {Version}, ManifestId: {ManifestId}, IsCrcMatch: {IsCrcMatch})",
            card.Name,
            card.Version,
            card.ManifestId,
            card.IsCrcMatch);

        SelectedClient = card.Client;
        SelectedManifestId = card.ManifestId;
        WasSuccessful = true;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
