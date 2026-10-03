using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;

namespace GenHub.Core.Interfaces.Tools.WorldBuilder;

/// <summary>
/// Validates map documents structurally (chunk presence, sizes, references).
/// </summary>
public interface IMapValidationService
{
    /// <summary>
    /// Validates a loaded document.
    /// </summary>
    /// <param name="map">The document to validate.</param>
    /// <returns>The validation result.</returns>
    ValidationResult Validate(WorldBuilderMap map);

    /// <summary>
    /// Computes the total world cash value (supply value across the map).
    /// Mirrors the engine formula: starting boxes per supply source times the
    /// value per box. Template data comes from game INI files.
    /// </summary>
    /// <param name="map">The document to evaluate.</param>
    /// <param name="valuePerSupplyBox">Cash value per supply box.</param>
    /// <param name="startingBoxesByTemplate">Starting boxes keyed by supply template name.</param>
    /// <returns>The total cash value.</returns>
    int ComputeWorldCash(WorldBuilderMap map, int valuePerSupplyBox, IReadOnlyDictionary<string, int> startingBoxesByTemplate);
}
