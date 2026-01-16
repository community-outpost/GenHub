namespace GenHub.Core.Models.Enums;

/// <summary>
/// Defines the type of publisher subscription.
/// </summary>
public enum SubscriptionType
{
    /// <summary>
    /// Legacy subscription pointing directly to a catalog.json file.
    /// No self-update capability for the provider definition itself.
    /// </summary>
    CatalogOnly = 0,

    /// <summary>
    /// Subscription pointing to a provider.json definition file.
    /// Supports self-updates and multiple catalog mirrors.
    /// </summary>
    ProviderDefinition = 1,
}
