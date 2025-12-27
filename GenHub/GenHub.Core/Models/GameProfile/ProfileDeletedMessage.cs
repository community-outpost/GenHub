namespace GenHub.Core.Models.GameProfile;

/// <summary>
/// Message sent when a game profile is deleted.
/// </summary>
/// <param name="ProfileId">The ID of the deleted profile.</param>
/// <param name="ProfileName">The name of the deleted profile.</param>
public record ProfileDeletedMessage(string ProfileId, string ProfileName);