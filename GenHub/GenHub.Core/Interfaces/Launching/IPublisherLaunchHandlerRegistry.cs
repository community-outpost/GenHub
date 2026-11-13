using GenHub.Core.Models.GameProfile;
using System.Collections.Generic;

namespace GenHub.Core.Interfaces.Launching;

/// <summary>
/// Registry for resolving publisher-specific launch handlers.
/// </summary>
public interface IPublisherLaunchHandlerRegistry
{
    /// <summary>
    /// Gets the appropriate publisher launch handler for the specified game profile.
    /// </summary>
    /// <remarks>
    /// Handlers are evaluated in registration order, stopping at the first non-default handler
    /// whose <see cref="IPublisherLaunchHandler.CanHandle"/> returns <c>true</c>. If no specific
    /// handler matches the profile, the registered default handler is returned.
    /// </remarks>
    /// <param name="profile">The game profile.</param>
    /// <returns>The matching publisher launch handler, or the default handler if no specific handler matches.</returns>
    IPublisherLaunchHandler GetHandler(GameProfile profile);

    /// <summary>
    /// Gets a registered handler by its publisher type identifier, if present.
    /// Intended for diagnostic inspection, tooling, and direct publisher lookup.
    /// </summary>
    /// <param name="publisherType">The publisher type string.</param>
    /// <returns>The matching publisher launch handler, or null if not found.</returns>
    IPublisherLaunchHandler? GetHandlerByPublisherType(string publisherType);

    /// <summary>
    /// Gets all registered publisher launch handlers.
    /// Intended for diagnostic inspection, configuration verification, and testing.
    /// </summary>
    /// <returns>A read-only list of all registered handlers.</returns>
    IReadOnlyList<IPublisherLaunchHandler> GetAllHandlers();
}
