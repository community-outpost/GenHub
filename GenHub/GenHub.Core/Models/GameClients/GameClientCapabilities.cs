using System;

namespace GenHub.Core.Models.GameClients;

/// <summary>
/// Bitwise capability flags indicating specific feature support for a game client executable.
/// </summary>
[Flags]
public enum GameClientCapabilities
{
    /// <summary>
    /// Baseline game client without modern extended CLI capabilities.
    /// </summary>
    None = 0,

    /// <summary>
    /// Supports direct replay launch via command line: -replay [file.rep].
    /// </summary>
    ReplayCliLaunch = 1 << 0,

    /// <summary>
    /// Supports mid-game checkpoint save emission via command line: -saveatframe [frame] -saveto [file.sav] -quitatframe [frame].
    /// </summary>
    CheckpointSaves = 1 << 1,

    /// <summary>
    /// Supports deterministic replay resumption from a checkpoint save: -loadsave [file.sav] -resumereplay [file.rep].
    /// </summary>
    ReplayResumption = 1 << 2,

    /// <summary>
    /// Supports taking over game state as a live player from a save: -loadsave [file.sav] -resumeas [slotIndex].
    /// </summary>
    PlayerTakeover = 1 << 3,

    /// <summary>
    /// All extended replay and checkpoint recovery capabilities combined.
    /// </summary>
    AllRecoveryFeatures = ReplayCliLaunch | CheckpointSaves | ReplayResumption | PlayerTakeover,
}
