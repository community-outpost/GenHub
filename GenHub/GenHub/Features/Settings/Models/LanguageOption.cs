using System.Globalization;

namespace GenHub.Features.Settings.Models;

/// <summary>
/// Represents a language option for UI selection.
/// </summary>
/// <param name="Culture">The underlying culture.</param>
/// <param name="DisplayName">The user-friendly display name of the language.</param>
public sealed record LanguageOption(
    CultureInfo Culture,
    string DisplayName)
{
    /// <inheritdoc/>
    public override string ToString() => DisplayName;
}
