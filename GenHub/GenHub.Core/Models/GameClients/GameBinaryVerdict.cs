using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.GameClients;

/// <summary>
/// The outcome of inspecting one game binary: its role plus a game type resolved from
/// engine markers. Zero Hour wins when exclusive markers prove it; Generals is returned
/// when only Generals engine tokens are present; conflicting or absent evidence yields
/// Unknown.
/// </summary>
/// <param name="Role">The classified file role.</param>
/// <param name="GameType">The resolved game type: Zero Hour, Generals, or Unknown.</param>
/// <param name="Reason">How the verdict was reached, for logging.</param>
public sealed record GameBinaryVerdict(GameBinaryRole Role, GameType GameType, string Reason);
