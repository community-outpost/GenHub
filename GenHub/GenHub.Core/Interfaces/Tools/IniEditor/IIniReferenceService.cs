using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.IniEditor;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.IniEditor;

/// <summary>
/// Indexes INI blocks from the open document, the open folder, and vanilla game data
/// for reference pickers and clone-from-reference actions.
/// </summary>
public interface IIniReferenceService
{
    /// <summary>
    /// Gets the current index entries.
    /// </summary>
    IReadOnlyList<IniReferenceEntry> Entries { get; }

    /// <summary>
    /// Gets a value indicating whether an index has been built.
    /// </summary>
    bool IsIndexed { get; }

    /// <summary>
    /// Rebuilds the index from the open document, folder, and vanilla game data.
    /// Folder and vanilla entries are cached until the folder changes or a rescan is forced.
    /// </summary>
    /// <param name="document">The open document, when any.</param>
    /// <param name="folderPath">The open folder, when any.</param>
    /// <param name="forceRescan">Whether to rescan cached folder and vanilla entries.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result with the indexed entry count.</returns>
    Task<OperationResult<int>> RebuildIndexAsync(IniDocument? document, string? folderPath, bool forceRescan = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets distinct block names of the given type across all sources.
    /// </summary>
    /// <param name="blockType">The block type name.</param>
    /// <returns>The matching block names.</returns>
    IReadOnlyList<string> GetNames(string blockType);

    /// <summary>
    /// Loads a deep copy of the referenced block for insertion into the open document.
    /// </summary>
    /// <param name="entry">The reference entry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result with the copied block, or null when it no longer exists.</returns>
    Task<OperationResult<IniBlock?>> CloneBlockAsync(IniReferenceEntry entry, CancellationToken cancellationToken = default);
}
