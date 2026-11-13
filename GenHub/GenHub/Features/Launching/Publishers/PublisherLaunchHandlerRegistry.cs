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
public class PublisherLaunchHandlerRegistry : IPublisherLaunchHandlerRegistry
{
    private readonly IPublisherLaunchHandler[] _handlers;
    private readonly DefaultPublisherLaunchHandler _defaultHandler;
    private readonly ILogger<PublisherLaunchHandlerRegistry> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherLaunchHandlerRegistry"/> class.
    /// </summary>
    /// <param name="handlers">The registered publisher launch handlers.</param>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown if handlers or logger is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown if a default publisher handler is not registered.</exception>
    public PublisherLaunchHandlerRegistry(
        IEnumerable<IPublisherLaunchHandler> handlers,
        ILogger<PublisherLaunchHandlerRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _handlers = handlers.ToArray();
        _defaultHandler = _handlers.OfType<DefaultPublisherLaunchHandler>().FirstOrDefault()
            ?? throw new InvalidOperationException("DefaultPublisherLaunchHandler must be registered in the publisher launch handler registry.");
    }

    /// <inheritdoc/>
    public IPublisherLaunchHandler GetHandler(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        for (var i = 0; i < _handlers.Length; i++)
        {
            var handler = _handlers[i];
            if (handler is not DefaultPublisherLaunchHandler && handler.CanHandle(profile))
            {
                _logger.LogDebug("[PublisherLaunchHandlerRegistry] Resolved handler {HandlerType} for profile {ProfileId}", handler.GetType().Name, profile.Id);
                return handler;
            }
        }

        return _defaultHandler;
    }

    /// <inheritdoc/>
    public IPublisherLaunchHandler? GetHandlerByPublisherType(string publisherType)
    {
        if (string.IsNullOrWhiteSpace(publisherType))
        {
            return null;
        }

        for (var i = 0; i < _handlers.Length; i++)
        {
            var handler = _handlers[i];
            if (string.Equals(handler.PublisherType, publisherType, StringComparison.OrdinalIgnoreCase))
            {
                return handler;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public IReadOnlyList<IPublisherLaunchHandler> GetAllHandlers() => _handlers.ToArray();
}
