using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Workspace;

/// <summary>
/// Creates and removes the links that mount live content into a workspace.
/// </summary>
public interface ILiveWorkspaceLinker
{
    /// <summary>
    /// Ensures that a workspace entry is a link to the supplied source file.
    /// </summary>
    /// <param name="workspaceRoot">The absolute workspace root.</param>
    /// <param name="relativePath">The workspace-relative path.</param>
    /// <param name="sourcePath">The absolute source file path.</param>
    /// <returns>Success, or a failure describing why the link could not be created.</returns>
    OperationResult EnsureLink(string workspaceRoot, string relativePath, string sourcePath);

    /// <summary>
    /// Ensures that a workspace entry is a private copy of the source file. Used for files
    /// the game may write, so writes never reach the source folder.
    /// </summary>
    /// <param name="workspaceRoot">The absolute workspace root.</param>
    /// <param name="relativePath">The workspace-relative path.</param>
    /// <param name="sourcePath">The absolute source file path.</param>
    /// <returns>Success, or a failure describing why the copy could not be refreshed.</returns>
    OperationResult EnsureCopy(string workspaceRoot, string relativePath, string sourcePath);

    /// <summary>
    /// Deletes a workspace link when it points into one of the supplied live roots.
    /// </summary>
    /// <param name="linkPath">The workspace entry to inspect.</param>
    /// <param name="liveRoots">The live roots a link may belong to.</param>
    /// <returns><c>true</c> when the entry was a live link and was deleted.</returns>
    bool TryDeleteLiveLink(string linkPath, IReadOnlyList<string> liveRoots);

    /// <summary>
    /// Determines whether an existing workspace entry is a link to the supplied source.
    /// </summary>
    /// <param name="path">The workspace entry.</param>
    /// <param name="sourcePath">The expected source file.</param>
    /// <returns><c>true</c> when the entry is a link to the source.</returns>
    bool IsLinkTo(string path, string sourcePath);
}
