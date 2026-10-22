using GenHub.Core.Interfaces.Content;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Features.Content.Services.Reconciliation;

/// <summary>
/// Default implementation of the publisher reconciler registry.
/// </summary>
public class PublisherReconcilerRegistry(
    IEnumerable<IPublisherReconciler> reconcilers,
    IGenericCatalogProfileReconciler? genericReconciler = null) : IPublisherReconcilerRegistry
{
    /// <inheritdoc/>
    public IPublisherReconciler? GetReconciler(string publisherType)
    {
        if (string.IsNullOrWhiteSpace(publisherType))
        {
            return null;
        }

        var specific = reconcilers.FirstOrDefault(r => string.Equals(r.PublisherType, publisherType, StringComparison.OrdinalIgnoreCase));
        if (specific != null)
        {
            return specific;
        }

        return genericReconciler;
    }
}
