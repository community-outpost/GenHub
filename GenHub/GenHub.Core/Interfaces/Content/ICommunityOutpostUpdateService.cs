namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Interface for Community Outpost update service.
/// </summary>
public interface ICommunityOutpostUpdateService : IContentUpdateService
{
    /// <summary>
    /// Invalidates any cached update check result.
    /// </summary>
    void InvalidateCache();
}
