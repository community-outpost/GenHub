using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Models.GameProfile;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Features.Launching.Publishers;

/// <summary>
/// Registry managing publisher launch handlers and resolving the matching handler for a profile.
/// </summary>
public class PublisherLaunchHandlerRegistry(
    IReadOnlyList<IPublisherLaunchHandler> handlers,
    ILogger<PublisherLaunchHandlerRegistry> logger) : IPublisherLaunchHandlerRegistry
{
    /// <inheritdoc/>
    public IPublisherLaunchHandler GetHandler(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        for (var i = 0; i < handlers.Count; i++)
        {
            var handler = handlers[i];
            if (handler is not DefaultPublisherLaunchHandler && handler.CanHandle(profile))
            {
                logger.LogDebug("[PublisherLaunchHandlerRegistry] Resolved handler {HandlerType} for profile {ProfileId}", handler.GetType().Name, profile.Id);
                return handler;
            }
        }

        return handlers.OfType<DefaultPublisherLaunchHandler>().FirstOrDefault() ?? new DefaultPublisherLaunchHandler();
    }

    /// <inheritdoc/>
    public IPublisherLaunchHandler? GetHandlerByPublisherType(string publisherType)
    {
        if (string.IsNullOrWhiteSpace(publisherType))
        {
            return null;
        }

        for (var i = 0; i < handlers.Count; i++)
        {
            var handler = handlers[i];
            if (string.Equals(handler.PublisherType, publisherType, StringComparison.OrdinalIgnoreCase))
            {
                return handler;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public IReadOnlyList<IPublisherLaunchHandler> GetAllHandlers() => handlers;
}
