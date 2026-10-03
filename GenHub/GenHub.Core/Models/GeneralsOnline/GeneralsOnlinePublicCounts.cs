namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Public lobby and player counts from the Monitoring BasicStats endpoint.
/// </summary>
/// <param name="Lobbies">The number of public lobbies.</param>
/// <param name="Players">The number of online players.</param>
public sealed record GeneralsOnlinePublicCounts(int Lobbies, int Players);
