using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;

namespace GenHub.Core.Helpers;

/// <summary>
/// Provides shared helpers for resolving game client telemetry attributes.
/// </summary>
public static class GameClientTelemetryHelper
{
    private const string DefaultPublisher = "Retail";
    private const string GenericPublisher = "Publisher";

    /// <summary>
    /// Resolves the canonical publisher name for a game client reference.
    /// </summary>
    /// <param name="gameClient">The game client reference.</param>
    /// <returns>The resolved publisher identifier.</returns>
    public static string ResolvePublisher(GameClient? gameClient)
    {
        if (gameClient == null)
        {
            return DefaultPublisher;
        }

        if (!string.IsNullOrWhiteSpace(gameClient.PublisherType))
        {
            return gameClient.PublisherType;
        }

        if (gameClient.IsPublisherClient)
        {
            return GenericPublisher;
        }

        if (!string.IsNullOrWhiteSpace(gameClient.InstallationId))
        {
            return gameClient.InstallationId;
        }

        return DefaultPublisher;
    }

    /// <summary>
    /// Resolves the canonical publisher name for a game profile.
    /// </summary>
    /// <param name="profile">The game profile.</param>
    /// <returns>The resolved publisher identifier.</returns>
    public static string ResolvePublisher(GameProfile? profile) =>
        ResolvePublisher(profile?.GameClient);
}
