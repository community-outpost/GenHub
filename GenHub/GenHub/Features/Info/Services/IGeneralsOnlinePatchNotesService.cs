using System.Collections.Generic;
using System.Threading.Tasks;
using GenHub.Core.Models.Info;

namespace GenHub.Features.Info.Services;

/// <summary>
/// Service for fetching and parsing Generals Online patch notes.
/// </summary>
public interface IGeneralsOnlinePatchNotesService
{
    /// <summary>
    /// Gets all patch notes from the Generals Online website.
    /// </summary>
    /// <returns>A collection of patch notes.</returns>
    Task<IEnumerable<PatchNote>> GetPatchNotesAsync();

    /// <summary>
    /// Fetches the detailed changes for a specific patch note.
    /// </summary>
    /// <param name="patchNote">The patch note to fetch details for.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task GetPatchDetailsAsync(PatchNote patchNote);
}
