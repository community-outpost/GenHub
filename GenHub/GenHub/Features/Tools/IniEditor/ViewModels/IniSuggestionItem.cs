namespace GenHub.Features.Tools.IniEditor.ViewModels;

/// <summary>
/// A single autocomplete suggestion with its display icon.
/// </summary>
/// <param name="Label">The suggestion text committed to the field.</param>
/// <param name="IconKind">The Material icon kind describing the suggestion source.</param>
public sealed record IniSuggestionItem(string Label, string IconKind)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}
