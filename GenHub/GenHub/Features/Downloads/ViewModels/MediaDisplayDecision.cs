namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// The presentation decision for a gallery media item.
/// </summary>
/// <param name="Action">How the item should be presented.</param>
/// <param name="Url">The URL to present, when applicable.</param>
public sealed record MediaDisplayDecision(MediaDisplayAction Action, string? Url);
