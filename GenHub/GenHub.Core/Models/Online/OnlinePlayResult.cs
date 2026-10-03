namespace GenHub.Core.Models.Online;

/// <summary>
/// Successful Play outcome from the Online tab.
/// </summary>
/// <param name="ProfileId">The launched profile identifier.</param>
/// <param name="ProfileName">The launched profile display name.</param>
/// <param name="NetworkName">The network the player joined from.</param>
public sealed record OnlinePlayResult(string ProfileId, string ProfileName, string NetworkName);
