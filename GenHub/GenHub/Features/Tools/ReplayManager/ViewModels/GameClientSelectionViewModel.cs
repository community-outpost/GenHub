using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
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
    ILogger<GameClientSelectionViewModel> logger) : ObservableObject
{
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
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public Task LoadClientsAsync(GameType targetGame, string replayFileName, CancellationToken ct = default)
        => LoadClientsAsync(targetGame, replayFileName, replay: null, ct);

    /// <summary>
    /// Loads available game clients for the specified game type and replay file.
    /// </summary>
    /// <param name="targetGame">The game type (Generals or Zero Hour).</param>
    /// <param name="replayFileName">The name of the replay file.</param>
    /// <param name="replay">The replay file containing CRC and matching info.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task LoadClientsAsync(
        GameType targetGame,
        string replayFileName,
        ReplayFile? replay = null,
        CancellationToken ct = default)
    {
        TargetGame = targetGame;
        ReplayFileName = replayFileName;

        InitializeCrcInfo(replay);

        IsLoading = true;
        _allClients.Clear();
        FilteredClients.Clear();

        try
        {
            logger.LogInformation(
                "[ReplayManager] Discovering game clients for {GameType} (Replay: '{ReplayFile}', ExeCrc: {ExeCrc}, IniCrc: {IniCrc})",
                targetGame,
                replayFileName,
                ReplayExeCrc,
                ReplayIniCrc);

            var discoveredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var matchedClient = ResolveMatchedClient(replay);
            if (matchedClient != null)
            {
                AddMatchedClientCard(matchedClient, targetGame, discoveredKeys);
            }

            AddRetailClientCard(targetGame, replay, discoveredKeys);

            await DiscoverManifestClientsAsync(targetGame, matchedClient, discoveredKeys, ct);
            await DiscoverProfileClientsAsync(targetGame, matchedClient, discoveredKeys, ct);

            UpdateCompatibilityCounts();
            ApplyFilter();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Determines whether the given manifest matches the specified target game.
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

        var tokens = manifest.Id.Value.Split('.');
        if (targetGame == GameType.ZeroHour)
        {
            return tokens.Any(t => string.Equals(t, "zerohour", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(t, "zh", StringComparison.OrdinalIgnoreCase) ||
                                   t.StartsWith("zerohour-", StringComparison.OrdinalIgnoreCase) ||
                                   t.StartsWith("zh-", StringComparison.OrdinalIgnoreCase) ||
                                   t.EndsWith("-zerohour", StringComparison.OrdinalIgnoreCase) ||
                                   t.EndsWith("-zh", StringComparison.OrdinalIgnoreCase));
        }

        if (targetGame == GameType.Generals)
        {
            return tokens.Any(t => string.Equals(t, "generals", StringComparison.OrdinalIgnoreCase) ||
                                   t.StartsWith("generals-", StringComparison.OrdinalIgnoreCase) ||
                                   t.EndsWith("-generals", StringComparison.OrdinalIgnoreCase));
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
            Category: "CRC Compatible",
            ExecutablePath: string.Empty,
            Description: $"Exact match for replay CRC (EXE: {ReplayExeCrc}, INI: {ReplayIniCrc})",
            OnSelect: OnClientSelected,
            IsCrcMatch: true));

        _allClients.Add(card);
        if (!string.IsNullOrEmpty(matchedClient.ManifestId))
        {
            discoveredKeys.Add(matchedClient.ManifestId);
        }
    }

    private void AddRetailClientCard(GameType targetGame, ReplayFile? replay, ISet<string> discoveredKeys)
    {
        var isRetailZeroHour = targetGame == GameType.ZeroHour && replay?.ExeCrc == 0x76B66B82;
        var isRetailGenerals = targetGame == GameType.Generals && replay?.ExeCrc == 0x36B86C82;
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
            Category: isRetailMatch ? "CRC Compatible" : "Retail Fallback",
            ExecutablePath: string.Empty,
            Description: retailDescription,
            OnSelect: OnClientSelected,
            IsCrcMatch: isRetailMatch));

        if (discoveredKeys.Add(retailName))
        {
            _allClients.Add(retailCard);
        }
    }

    private void UpdateCompatibilityCounts()
    {
        CompatibleCount = _allClients.Count(c => c.IsCrcMatch);
        HasCompatibleCrcClients = CompatibleCount > 0;

        if (!HasCompatibleCrcClients)
        {
            ShowAllClients = true;
        }
    }

    private string GetPublisherDisplayName(PublisherInfo? publisher)
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

                var isCrcMatch = false;
                if (matchedClient != null)
                {
                    var versionMatch = !string.IsNullOrEmpty(matchedClient.Version) &&
                                       !string.IsNullOrEmpty(manifest.Version) &&
                                       string.Equals(matchedClient.Version.TrimStart('0'), manifest.Version.TrimStart('0'), StringComparison.OrdinalIgnoreCase);

                    var idMatch = string.Equals(manifest.Id.Value, matchedClient.ManifestId, StringComparison.OrdinalIgnoreCase);

                    isCrcMatch = idMatch || (versionMatch && string.Equals(manifest.Publisher?.PublisherType, matchedClient.Publisher, StringComparison.OrdinalIgnoreCase));
                }

                _allClients.Add(CreateManifestGameClientCard(manifest, targetGame, isCrcMatch));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[ReplayManager] Error querying manifests for game clients");
        }
    }

    private GameClientCardViewModel CreateManifestGameClientCard(ContentManifest manifest, GameType targetGame, bool isCrcMatch)
    {
        var entryResolution = ManifestVariantResolver.ResolveEntryPoint(manifest);
        var relExePath = entryResolution.Success && !string.IsNullOrWhiteSpace(entryResolution.RelativePath)
            ? entryResolution.RelativePath
            : string.Empty;

        var publisherName = GetPublisherDisplayName(manifest.Publisher);
        var resolvedGameType = manifest.TargetGame != GameType.Unknown ? manifest.TargetGame : targetGame;
        var client = new GameClient
        {
            Id = manifest.Id.Value,
            Name = manifest.Name,
            Version = manifest.Version,
            PublisherType = publisherName,
            GameType = resolvedGameType,
            ExecutablePath = relExePath,
        };

        var description = !string.IsNullOrWhiteSpace(manifest.Metadata?.Description)
            ? manifest.Metadata.Description
            : $"Catalog manifest {manifest.Id.Value}";

        var category = isCrcMatch ? "CRC Compatible" : "Catalog Manifest";

        return new GameClientCardViewModel(new GameClientCardParameters(
            Client: client,
            ManifestId: manifest.Id.Value,
            Name: manifest.Name,
            Version: manifest.Version,
            Publisher: publisherName,
            Category: category,
            ExecutablePath: relExePath,
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
                var client = profile.GameClient;
                if (client == null)
                {
                    continue;
                }

                var clientName = !string.IsNullOrWhiteSpace(client.Name)
                    ? client.Name
                    : profile.Name;

                var exePath = client.ExecutablePath ?? string.Empty;
                var dedupeKey = $"{clientName}|{exePath}";

                if (!discoveredKeys.Add(dedupeKey))
                {
                    continue;
                }

                var isCrcMatch = false;
                if (matchedClient != null && !string.IsNullOrEmpty(matchedClient.Version) && !string.IsNullOrEmpty(client.Version))
                {
                    isCrcMatch = string.Equals(matchedClient.Version.TrimStart('0'), client.Version.TrimStart('0'), StringComparison.OrdinalIgnoreCase);
                }

                _allClients.Add(new GameClientCardViewModel(new GameClientCardParameters(
                    Client: client,
                    ManifestId: client.Id,
                    Name: clientName,
                    Version: client.Version ?? "Custom",
                    Publisher: client.PublisherType ?? "Local Profile",
                    Category: isCrcMatch ? "CRC Compatible" : "Local Profile",
                    ExecutablePath: exePath,
                    Description: $"Configured in existing profile '{profile.Name}'",
                    OnSelect: OnClientSelected,
                    IsCrcMatch: isCrcMatch)));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[ReplayManager] Error querying profiles for game clients");
        }
    }

    private void ApplyFilter()
    {
        var query = SearchText?.Trim();
        IEnumerable<GameClientCardViewModel> candidates = _allClients;

        if (!ShowAllClients && HasCompatibleCrcClients)
        {
            candidates = candidates.Where(c => c.IsCrcMatch);
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
