namespace GenHub.Core.Models.GameProfile;

/// <summary>
/// Message sent when an existing game profile has been updated.
/// </summary>
/// <param name="Profile">The updated game profile.</param>
public record ProfileUpdatedMessage(GameProfile Profile);
