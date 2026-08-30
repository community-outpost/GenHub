namespace GenHub.Core.Models.GameProfile;

/// <summary>
/// Message sent when a game profile is launched.
/// </summary>
/// <param name="ProfileId">The ID of the launched profile.</param>
/// <param name="ProcessId">The process ID of the game process.</param>
public record ProfileLaunchedMessage(string ProfileId, int ProcessId);
