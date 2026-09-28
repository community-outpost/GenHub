using GenHub.Core.Constants;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using System;

namespace GenHub.Core.Helpers;

/// <summary>
/// Provides shared helpers for resolving game client telemetry attributes.
/// </summary>
public static class GameClientTelemetryHelper
{
    /// <summary>
    /// Fallback publisher identifier for base or retail game clients without publisher attribution.
    /// </summary>
    public const string DefaultPublisher = PublisherTypeConstants.Retail;

    /// <summary>
    /// Resolves the canonical publisher name for a game client reference.
    /// </summary>
    /// <param name="gameClient">The game client reference.</param>
    /// <returns>The resolved publisher identifier, normalized to lowercase.</returns>
    public static string ResolvePublisher(GameClient? gameClient)
    {
        if (gameClient == null)
        {
            return DefaultPublisher;
        }

        if (!string.IsNullOrWhiteSpace(gameClient.PublisherType))
        {
            return gameClient.PublisherType.Trim().ToLowerInvariant();
        }

        if (gameClient.IsPublisherClient)
        {
            return PublisherTypeConstants.Publisher;
        }

        return DefaultPublisher;
    }

    /// <summary>
    /// Resolves the canonical publisher name for a game profile.
    /// </summary>
    /// <param name="profile">The game profile.</param>
    /// <returns>The resolved publisher identifier, normalized to lowercase.</returns>
    public static string ResolvePublisher(GameProfile? profile) =>
        ResolvePublisher(profile?.GameClient);
}
