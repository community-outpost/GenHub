using System.IO;

namespace GenHub.Core.Interfaces.Tools.ReplayManager;

/// <summary>
/// Service for validating ZIP files.
/// </summary>
public interface IZipValidationService
{
    /// <summary>
    /// Validates if the given file path points to a valid ZIP archive.
    /// </summary>
    /// <param name="zipPath">The path to the ZIP file.</param>
    /// <returns>A tuple with validation result and error message if invalid.</returns>
    (bool IsValid, string? ErrorMessage) ValidateZip(string zipPath);
}