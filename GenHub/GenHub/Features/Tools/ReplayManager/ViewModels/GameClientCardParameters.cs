using System;
using GenHub.Core.Models.GameClients;

namespace GenHub.Features.Tools.ReplayManager.ViewModels;

/// <summary>
/// Parameters for initializing a <see cref="GameClientCardViewModel"/>.
/// </summary>
/// <param name="Client">The game client model.</param>
/// <param name="ManifestId">The manifest ID if catalog backed.</param>
/// <param name="Name">The display name.</param>
/// <param name="Version">The client version.</param>
/// <param name="Publisher">The publisher name.</param>
/// <param name="Category">The category name.</param>
/// <param name="ExecutablePath">The executable path.</param>
/// <param name="Description">The description.</param>
/// <param name="OnSelect">Callback when the client card is selected.</param>
public sealed record GameClientCardParameters(
    GameClient Client,
    string? ManifestId,
    string Name,
    string Version,
    string Publisher,
    string Category,
    string ExecutablePath,
    string Description,
    Action<GameClientCardViewModel> OnSelect);
