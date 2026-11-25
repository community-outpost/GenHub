using GenHub.Core.Models.Enums;
using System.Collections.Generic;

namespace GenHub.Core.Interfaces.GameInstallations;

/// <summary>
/// Provides candidate search directories for discovering game installations on the current platform.
/// </summary>
public interface IInstallationSearchPathProvider
{
    /// <summary>
    /// Gets candidate directories to search for installations of the specified type on the current platform.
    /// </summary>
    /// <param name="installationType">The type of game installation.</param>
    /// <returns>A collection of directory paths to search.</returns>
    IReadOnlyList<string> GetSearchPaths(GameInstallationType installationType);
}
