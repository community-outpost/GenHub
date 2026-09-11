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
    /// <param name="parameters">The card configuration parameters.</param>
    public GameClientCardViewModel(GameClientCardParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        Client = parameters.Client ?? throw new ArgumentNullException(nameof(parameters));
        ManifestId = parameters.ManifestId;
        Name = parameters.Name;
        Version = parameters.Version;
        Publisher = parameters.Publisher;
        Category = parameters.Category;
        ExecutablePath = parameters.ExecutablePath;
        Description = parameters.Description;
        SelectCommand = new RelayCommand(() => parameters.OnSelect(this));
    }
}
