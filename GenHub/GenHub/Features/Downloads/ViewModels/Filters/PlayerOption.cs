namespace GenHub.Features.Downloads.ViewModels.Filters;

/// <summary>
/// Represents an option in a player count dropdown.
/// </summary>
/// <param name="Display">The display text.</param>
/// <param name="Value">The underlying filter value.</param>
public record PlayerOption(string Display, int? Value);
