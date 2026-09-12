namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Registry for resolving publisher reconcilers by publisher type.
/// </summary>
public interface IPublisherReconcilerRegistry
{
    /// <summary>
    /// Gets the reconciler for the specified publisher type.
    /// </summary>
    /// <param name="publisherType">The publisher type string.</param>
    /// <returns>The <see cref="IPublisherReconciler"/> or null if not found.</returns>
    IPublisherReconciler? GetReconciler(string publisherType);
}
