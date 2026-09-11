using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.GameClients;

namespace GenHub.Features.Tools.ReplayManager.ViewModels;

/// <summary>
/// Card ViewModel representing an available game client candidate for profile creation.
/// </summary>
public sealed partial class GameClientCardViewModel : ObservableObject
{
    /// <summary>
    /// Gets the underlying game client model.
    /// </summary>
    public GameClient Client { get; }

    /// <summary>
    /// Gets the optional manifest ID associated with this client.
    /// </summary>
    public string? ManifestId { get; }

    /// <summary>
    /// Gets the display name of the client.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the version string.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Gets the publisher or source of this client.
    /// </summary>
    public string Publisher { get; }

    /// <summary>
    /// Gets the category (e.g. "Detected Installation", "Local Profile / Custom", "Catalog Manifest").
    /// </summary>
    public string Category { get; }

    /// <summary>
    /// Gets the executable path or relative path.
    /// </summary>
    public string ExecutablePath { get; }

    /// <summary>
    /// Gets the description of this client candidate.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the command executed to select this client.
    /// </summary>
    public IRelayCommand SelectCommand { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GameClientCardViewModel"/> class.
    /// </summary>
    /// <param name="client">The game client model.</param>
    /// <param name="manifestId">The manifest ID if catalog backed.</param>
    /// <param name="name">The display name.</param>
    /// <param name="version">The client version.</param>
    /// <param name="publisher">The publisher name.</param>
    /// <param name="category">The category name.</param>
    /// <param name="executablePath">The executable path.</param>
    /// <param name="description">The description.</param>
    /// <param name="onSelect">Callback when the client card is selected.</param>
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
        Client = client ?? throw new ArgumentNullException(nameof(client));
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
