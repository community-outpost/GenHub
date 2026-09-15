using System.Collections.Generic;
using System.Threading.Tasks;
using GenHub.Core.Models.Info;

namespace GenHub.Core.Interfaces.Info;

/// <summary>
/// Provides informational content about GenHub features.
/// </summary>
public interface IInfoContentProvider
{
    /// <summary>
    /// Gets all available info sections.
    /// </summary>
    /// <returns>A list of info sections.</returns>
    Task<IEnumerable<InfoSection>> GetAllSectionsAsync();

    /// <summary>
    /// Gets a specific info section by ID.
    /// </summary>
    /// <param name="sectionId">The section identifier.</param>
    /// <returns>The info section if found; otherwise, null.</returns>
    Task<InfoSection?> GetSectionAsync(string sectionId);
}
