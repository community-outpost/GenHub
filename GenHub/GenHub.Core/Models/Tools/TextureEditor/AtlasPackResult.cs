namespace GenHub.Core.Models.Tools.TextureEditor;

/// <summary>
/// Represents the geometry result of packing sprites into a texture atlas.
/// </summary>
/// <param name="SheetWidth">The power-of-two sheet width in pixels.</param>
/// <param name="SheetHeight">The power-of-two sheet height in pixels.</param>
/// <param name="Placements">The sprite placements inside the sheet.</param>
public sealed record AtlasPackResult(int SheetWidth, int SheetHeight, IReadOnlyList<AtlasPlacement> Placements);
