using System.Collections.Generic;
using System.Threading;
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

    /// <summary>
    /// Fetches and formats the patch notes for a given release version into plain text.
    /// </summary>
    /// <param name="version">The release version (e.g. "082826" or "082826_QFE1").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The formatted patch notes, or null if retrieval fails.</returns>
    Task<string?> GetPatchNotesFormattedAsync(string version, CancellationToken cancellationToken = default);
}
