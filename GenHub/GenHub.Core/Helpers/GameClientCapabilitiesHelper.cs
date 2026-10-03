using GenHub.Core.Constants;
using GenHub.Core.Models.GameClients;
using System;

namespace GenHub.Core.Helpers;

/// <summary>
/// Helper methods for evaluating and inferring game client engine capabilities.
/// </summary>
public static class GameClientCapabilitiesHelper
{
    /// <summary>
    /// Determines whether the specified publisher identifier corresponds to The Super Hackers or legacy Super Hackers.
    /// </summary>
    /// <param name="publisher">The publisher identifier or name.</param>
    /// <returns><see langword="true"/> if the publisher is Super Hackers; otherwise, <see langword="false"/>.</returns>
    public static bool IsSuperHackersPublisher(string? publisher)
    {
        if (string.IsNullOrWhiteSpace(publisher))
        {
            return false;
        }

        return string.Equals(publisher, PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(publisher, PublisherTypeConstants.LegacySuperHackers, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Infers extended engine capabilities based on client publisher, identifier, and display name.
    /// </summary>
    /// <param name="publisher">The publisher type or identifier.</param>
    /// <param name="id">The client or manifest ID.</param>
    /// <param name="name">The client or profile display name.</param>
    /// <returns>The inferred <see cref="GameClientCapabilities"/> flags.</returns>
    public static GameClientCapabilities InferCapabilities(string? publisher, string? id, string? name)
    {
        if (ContainsKeyword(id, name, ReplayManagerConstants.CapabilityRecoveryKeyword))
        {
            return GameClientCapabilities.AllRecoveryFeatures;
        }

        var capabilities = GameClientCapabilities.None;

        if (ContainsKeyword(id, name, ReplayManagerConstants.CapabilityCheckpointKeyword))
        {
            capabilities |= GameClientCapabilities.CheckpointSaves;
        }

        if (ContainsKeyword(id, name, ReplayManagerConstants.CapabilityTakeoverKeyword))
        {
            capabilities |= GameClientCapabilities.PlayerTakeover;
        }

        return capabilities;
    }

    private static bool ContainsKeyword(string? id, string? name, string keyword) =>
        (!string.IsNullOrEmpty(id) && id.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
        (!string.IsNullOrEmpty(name) && name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
}
