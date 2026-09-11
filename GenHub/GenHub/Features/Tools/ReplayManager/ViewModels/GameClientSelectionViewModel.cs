using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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
/// ViewModel representing an available game client option card.
/// </summary>
public sealed partial class GameClientCardViewModel : ObservableObject
{
    /// <summary>
    /// Gets the game client definition.
    /// </summary>
    public GameClient Client { get; }

    /// <summary>
    /// Gets the optional manifest ID if backed by a catalog manifest.
    /// </summary>
    public string? ManifestId { get; }

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the client version.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Gets the publisher or source.
    /// </summary>
    public string Publisher { get; }

    /// <summary>
    /// Gets the source category tag.
    /// </summary>
    public string Category { get; }

    /// <summary>
    /// Gets the executable path or relative binary name.
    /// </summary>
    public string ExecutablePath { get; }

    /// <summary>
    /// Gets the description text.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the command executed to select this client.
    /// </summary>
    public IRelayCommand SelectCommand { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref=\"GameClientCardViewModel\"/> class.
    /// </summary>
    public GameClientCardViewModel(
        GameClient client,
        string? manifestId,
        string name,
        string version,
        string publisher,
        string category,
        string executablePath,
        string description,
        Action<GameClientCardViewModel> onSelect)
    {
        Client = client;
        ManifestId = manifestId;
        Name = name;
        Version = version;
        Publisher = publisher;
        Category = category;
        ExecutablePath = executablePath;
        Description = description;
        SelectCommand = new RelayCommand(() => onSelect(this));
    }
}

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

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    /// <summary>
    /// Loads all available game clients for the specified game type and replay.
    /// </summary>
    /// <param name=\"targetGame\">The game type (Generals or Zero Hour).</param>
    /// <param name=\"replayFileName\">The name of the replay file.</param>
    /// <param name=\"ct\">Cancellation token.</param>
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
                \"[ReplayManager] Discovering available game clients for {GameType} (Replay: '{ReplayFile}')\",
                targetGame,
                replayFileName);

            var discoveredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Discover local / custom game clients from existing profiles (Community Patch, MP Recovery, etc.)
            try
            {
                var profilesResult = await profileManager.GetAllProfilesAsync(ct);
                if (profilesResult.Success && profilesResult.Data != null)
                {
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
                        var dedupeKey = $\"{clientName}|{exePath}\";

                        if (!discoveredKeys.Add(dedupeKey))
                        {
                            continue;
                        }

                        _allClients.Add(new GameClientCardViewModel(
                            client: client,
                            manifestId: client.Id,
                            name: clientName,
                            version: client.Version ?? \"Custom\",
                            publisher: client.PublisherType ?? \"Local / Profile\",
                            category: \"Local Profile / Custom\",
                            executablePath: exePath,
                            description: $\"Configured in profile '{profile.Name}'\",
                            onSelect: OnClientSelected));
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, \"[ReplayManager] Error querying profiles for game clients\");
            }

            // 2. Discover clients from detected game installations
            try
            {
                var installationsResult = await installationService.GetAllInstallationsAsync(ct);
                if (installationsResult.Success && installationsResult.Data != null)
                {
                    foreach (var install in installationsResult.Data)
                    {
                        var isSupported = targetGame == GameType.ZeroHour ? install.HasZeroHour : install.HasGenerals;
                        if (!isSupported)
                        {
                            continue;
                        }

                        foreach (var client in install.AvailableGameClients.Where(c => c.GameType == targetGame))
                        {
                            var clientName = !string.IsNullOrWhiteSpace(client.Name)
                                ? client.Name
                                : $\"{targetGame} ({install.InstallationType})\";

                            var exePath = client.ExecutablePath ?? string.Empty;
                            var dedupeKey = $\"{clientName}|{exePath}\";

                            if (!discoveredKeys.Add(dedupeKey))
                            {
                                continue;
                            }

                            _allClients.Add(new GameClientCardViewModel(
                                client: client,
                                manifestId: client.Id,
                                name: clientName,
                                version: client.Version ?? \"Retail\",
                                publisher: client.PublisherType ?? install.InstallationType.ToString(),
                                category: \"Detected Installation\",
                                executablePath: exePath,
                                description: $\"Installation at {install.InstallationPath}\",
                                onSelect: OnClientSelected));
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, \"[ReplayManager] Error querying installations for game clients\");
            }

            // 3. Discover clients from Content Manifest Pool (TheSuperHackers, GeneralsOnline, Community Outpost, etc.)
            try
            {
                var manifestsResult = await manifestPool.GetAllManifestsAsync(ct);
                if (manifestsResult.Success && manifestsResult.Data != null)
                {
                    foreach (var manifest in manifestsResult.Data.Where(m => m.ContentType == ContentType.GameClient))
                    {
                        var matchesGame = manifest.TargetGame == targetGame ||
                                          manifest.TargetGame == GameType.Unknown ||
                                          manifest.Id.Value.Contains(targetGame.ToString(), StringComparison.OrdinalIgnoreCase);

                        if (!matchesGame)
                        {
                            continue;
                        }

                        var dedupeKey = $\"{manifest.Name}|{manifest.Id.Value}\";
                        if (!discoveredKeys.Add(dedupeKey))
                        {
                            continue;
                        }

                        var entryResolution = ManifestVariantResolver.ResolveEntryPoint(manifest);
                        var relExePath = entryResolution.Success && !string.IsNullOrWhiteSpace(entryResolution.RelativePath)
                            ? entryResolution.RelativePath
                            : string.Empty;

                        var client = new GameClient
                        {
                            Id = manifest.Id.Value,
                            Name = manifest.Name,
                            Version = manifest.Version.ToString(),
                            PublisherType = manifest.Publisher,
                            GameType = targetGame,
                            ExecutablePath = relExePath,
                        };

                        _allClients.Add(new GameClientCardViewModel(
                            client: client,
                            manifestId: manifest.Id.Value,
                            name: manifest.Name,
                            version: manifest.Version.ToString(),
                            publisher: manifest.Publisher,
                            category: \"Catalog Manifest\",
                            executablePath: relExePath,
                            description: manifest.Description ?? $\"Manifest {manifest.Id.Value}\",
                            onSelect: OnClientSelected));
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, \"[ReplayManager] Error querying manifests for game clients\");
            }

            logger.LogInformation(
                \"[ReplayManager] Found {Count} total game clients for {GameType}\",
                _allClients.Count,
                targetGame);

            ApplyFilter();
        }
        finally
        {
            IsLoading = false;
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
            \"[ReplayManager] User selected game client '{ClientName}' ({Publisher}, {Category})\",
            card.Name,
            card.Publisher,
            card.Category);

        SelectedClient = card.Client;
        SelectedManifestId = card.ManifestId;
        WasSuccessful = true;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        WasSuccessful = false;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
