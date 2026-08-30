using System;
using System.Collections.Generic;
using System.Linq;
using GenHub.Core.Interfaces.Content;

namespace GenHub.Features.Content.Services.Reconciliation;

/// <summary>
/// Default implementation of the publisher reconciler registry.
/// </summary>
public class PublisherReconcilerRegistry(IEnumerable<IPublisherReconciler> reconcilers) : IPublisherReconcilerRegistry
{
    /// <inheritdoc/>
    public IPublisherReconciler? GetReconciler(string publisherType)
    {
        if (string.IsNullOrWhiteSpace(publisherType))
        {
            return null;
        }

        return reconcilers.FirstOrDefault(r => string.Equals(r.PublisherType, publisherType, StringComparison.OrdinalIgnoreCase));
    }
}
