namespace GenHub.Features.Downloads.ViewModels.Filters;

/// <summary>
/// Represents a filter dropdown option.
/// </summary>
/// <param name="DisplayName">The display name shown in UI.</param>
/// <param name="Value">The value used in queries.</param>
public record FilterOption(string DisplayName, string Value);
