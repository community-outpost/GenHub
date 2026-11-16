namespace GenHub.Features.Tools.IniEditor.ViewModels;

/// <summary>
/// A single label/value row on the object canvas summary card.
/// </summary>
/// <param name="Label">The row label.</param>
/// <param name="Value">The row value.</param>
public sealed record IniCanvasSummaryRow(string Label, string Value);
