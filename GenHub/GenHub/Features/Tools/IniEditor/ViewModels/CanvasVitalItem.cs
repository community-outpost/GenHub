namespace GenHub.Features.Tools.IniEditor.ViewModels;

/// <summary>
/// Represents a label-value vital statistic item displayed in the 2-column uniform grid.
/// </summary>
/// <param name="Label">The display label (e.g. Health, Cost, Command).</param>
/// <param name="Value">The formatted value.</param>
/// <param name="AccentBrush">The resource key or color theme for the value.</param>
public sealed record CanvasVitalItem(string Label, string Value, string AccentBrush = "AccentBrush");
