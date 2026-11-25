namespace GenHub.Features.Tools.ViewModels.Dialogs;

/// <summary>
/// A single variant-axis choice: a well-known axis, an explicit none, or a custom free-text axis.
/// </summary>
/// <param name="Value">The axis identifier written to catalogs, or null for none.</param>
/// <param name="DisplayName">The localized display label.</param>
/// <param name="Description">The localized explanation with concrete examples.</param>
/// <param name="IsCustom">Whether selecting this option reveals a free-text input.</param>
public sealed record VariantAxisOption(string? Value, string DisplayName, string Description, bool IsCustom = false);
