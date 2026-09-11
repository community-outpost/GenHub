using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ReplayManager.ViewModels;

/// <summary>
/// ViewModel for selecting an available game client to create a dedicated profile for a replay.
/// Gathers game clients from local/custom profiles, detected installations, and catalog manifests.
/// </summary>
public sealed partial class GameClientSelectionViewModel(
    IGameInstallationService installationService,
    IGameProfileManager profileManager,
    IContentManifestPool manifestPool,
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
    /// Loads all available game clients for the specified game type and replay.
    /// </summary>
    /// <param name="targetGame">The game type (Generals or Zero Hour).</param>
    /// <param name="replayFileName">The name of the replay file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
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
                "[ReplayManager] Discovering available game clients for {GameType} (Replay: '{ReplayFile}')",
                targetGame,
                replayFileName);

            var discoveredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await DiscoverProfileClientsAsync(targetGame, discoveredKeys, ct);
            await DiscoverInstallationClientsAsync(targetGame, discoveredKeys, ct);
            await DiscoverManifestClientsAsync(targetGame, discoveredKeys, ct);

            logger.LogInformation(
                "[ReplayManager] Found {Count} total game clients for {GameType}",
                _allClients.Count,
                targetGame);

            ApplyFilter();
        }
        finally
        {
            IsLoading = false;
        }
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

    private async Task DiscoverProfileClientsAsync(
        GameType targetGame,
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

                _allClients.Add(new GameClientCardViewModel(new GameClientCardParameters(
                    Client: client,
                    ManifestId: client.Id,
                    Name: clientName,
                    Version: client.Version ?? "Custom",
                    Publisher: client.PublisherType ?? "Local / Profile",
                    Category: "Local Profile / Custom",
                    ExecutablePath: exePath,
                    Description: $"Configured in profile '{profile.Name}'",
                    OnSelect: OnClientSelected)));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[ReplayManager] Error querying profiles for game clients");
        }
    }

    private async Task DiscoverInstallationClientsAsync(
        GameType targetGame,
        HashSet<string> discoveredKeys,
        CancellationToken ct)
    {
        try
        {
            var installationsResult = await installationService.GetAllInstallationsAsync(ct);
            if (!installationsResult.Success || installationsResult.Data == null)
            {
                return;
            }

            foreach (var install in installationsResult.Data)
            {
                var clients = install.AvailableGameClients.Where(c => c.GameType == targetGame);
                foreach (var client in clients)
                {
                    var clientName = !string.IsNullOrWhiteSpace(client.Name)
                        ? client.Name
                        : $"{targetGame} ({install.InstallationType})";

                    var exePath = client.ExecutablePath ?? string.Empty;
                    var dedupeKey = $"{clientName}|{exePath}";

                    if (!discoveredKeys.Add(dedupeKey))
                    {
                        continue;
                    }

                    _allClients.Add(new GameClientCardViewModel(new GameClientCardParameters(
                        Client: client,
                        ManifestId: client.Id,
                        Name: clientName,
                        Version: client.Version ?? "Retail",
                        Publisher: client.PublisherType ?? install.InstallationType.ToString(),
                        Category: "Detected Installation",
                        ExecutablePath: exePath,
                        Description: $"Installation at {install.InstallationPath}",
                        OnSelect: OnClientSelected)));
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[ReplayManager] Error querying installations for game clients");
        }
    }

    private async Task DiscoverManifestClientsAsync(
        GameType targetGame,
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
                var matchesGame = manifest.TargetGame == targetGame ||
                                  manifest.TargetGame == GameType.Unknown ||
                                  manifest.Id.Value.Contains(targetGame.ToString(), StringComparison.OrdinalIgnoreCase);

                if (!matchesGame)
                {
                    continue;
                }

                var dedupeKey = $"{manifest.Name}|{manifest.Id.Value}";
                if (!discoveredKeys.Add(dedupeKey))
                {
                    continue;
                }

                var entryResolution = ManifestVariantResolver.ResolveEntryPoint(manifest);
                var relExePath = entryResolution.Success && !string.IsNullOrWhiteSpace(entryResolution.RelativePath)
                    ? entryResolution.RelativePath
                    : string.Empty;

                var publisherName = GetPublisherDisplayName(manifest.Publisher);
                var client = new GameClient
                {
                    Id = manifest.Id.Value,
                    Name = manifest.Name,
                    Version = manifest.Version,
                    PublisherType = publisherName,
                    GameType = targetGame,
                    ExecutablePath = relExePath,
                };

                var description = !string.IsNullOrWhiteSpace(manifest.Metadata?.Description)
                    ? manifest.Metadata.Description
                    : $"Manifest {manifest.Id.Value}";

                _allClients.Add(new GameClientCardViewModel(new GameClientCardParameters(
                    Client: client,
                    ManifestId: manifest.Id.Value,
                    Name: manifest.Name,
                    Version: manifest.Version,
                    Publisher: publisherName,
                    Category: "Catalog Manifest",
                    ExecutablePath: relExePath,
                    Description: description,
                    OnSelect: OnClientSelected)));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[ReplayManager] Error querying manifests for game clients");
        }
    }

    private void ApplyFilter()
    {
        var query = SearchText?.Trim();
        IEnumerable<GameClientCardViewModel> filtered = _allClients;

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(c =>
                c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Version.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.ExecutablePath.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        FilteredClients = new ObservableCollection<GameClientCardViewModel>(filtered);
    }

    private void OnClientSelected(GameClientCardViewModel card)
    {
        logger.LogInformation(
            "[ReplayManager] User selected game client '{ClientName}' ({Publisher}, {Category})",
            card.Name,
            card.Publisher,
            card.Category);

        SelectedClient = card.Client;
        SelectedManifestId = card.ManifestId;
        WasSuccessful = true;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
