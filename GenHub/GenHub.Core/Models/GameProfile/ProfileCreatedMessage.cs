namespace GenHub.Core.Models.GameProfile;

/// <summary>
/// Message sent when a new game profile is created.
/// </summary>
public record ProfileCreatedMessage(GameProfile Profile);