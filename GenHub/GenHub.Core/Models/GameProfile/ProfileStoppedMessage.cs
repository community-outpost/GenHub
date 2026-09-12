namespace GenHub.Core.Models.GameProfile;

/// <summary>
/// Message sent when a running game profile stops or its process exits.
/// </summary>
/// <param name="ProfileId">The ID of the stopped profile.</param>
/// <param name="ProcessId">The process ID of the stopped game process.</param>
public record ProfileStoppedMessage(string ProfileId, int ProcessId);
