namespace GenHub.Core.Models.Enums;

/// <summary>
/// Defines the type of content provider.
/// </summary>
public enum ProviderType
{
    /// <summary>
    /// Static provider with fixed publisher identity (GeneralsOnline, CommunityOutpost, TheSuperhackers).
    /// Discovers from a catalog/API, publishes under a single known identity.
    /// </summary>
    Static = 0,

    /// <summary>
    /// Dynamic provider where authors become publishers (GitHub, ModDB, CNCLabs).
    /// Discovers content from various authors, each author becomes a distinct publisher.
    /// </summary>
    Dynamic = 1,
}
