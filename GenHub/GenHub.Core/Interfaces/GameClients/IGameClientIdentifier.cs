using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using System.Collections.Generic;

namespace GenHub.Core.Interfaces.GameClients;

/// <summary>
/// Identifies a local executable and returns metadata about what it is.
/// Does NOT create manifests or download anything.
/// </summary>
public interface IGameClientIdentifier
{
    /// <summary>
    /// Gets the publisher this identifier handles.
    /// </summary>
    string PublisherId { get; }

    /// <summary>
    /// Checks if this identifier can handle the given executable.
    /// </summary>
    /// <param name="executablePath">The path to the executable file to check.</param>
    /// <returns>True if this identifier can handle the executable, false otherwise.</returns>
    bool CanIdentify(string executablePath);

    /// <summary>
    /// Identifies the executable and returns metadata.
    /// </summary>
    /// <param name="executablePath">The path to the executable file to identify.</param>
    /// <returns>Identification metadata if the executable is recognized, null otherwise.</returns>
    GameClientIdentification? Identify(string executablePath);

    /// <summary>
    /// Resolves the single entry point among one directory's file names.
    /// Bootstrapper publishers ship several runnable binaries per directory that form
    /// one client; the entry rule lives with the publisher instead of the detector.
    /// </summary>
    /// <param name="fileNames">The file names present in a single directory.</param>
    /// <returns>The entry point name as it appears on disk, or <c>null</c> when this publisher has no entry here.</returns>
    string? ResolveDirectoryEntryPoint(IEnumerable<string> fileNames) => null;

    /// <summary>
    /// Resolves the single entry point for a directory.
    /// Bootstrapper publishers can inspect directory configuration to determine
    /// which executable acts as the active entry point.
    /// </summary>
    /// <param name="directory">The directory being inspected.</param>
    /// <param name="fileNames">The file names present in the directory.</param>
    /// <returns>The entry point name as it appears on disk, or <c>null</c> when this publisher has no entry here.</returns>
    string? ResolveDirectoryEntryPoint(string directory, IEnumerable<string> fileNames) =>
        ResolveDirectoryEntryPoint(fileNames);

    /// <summary>
    /// Determines whether an executable belongs to this publisher's client as
    /// non-launchable content (wrapped binaries, helpers). Companions are skipped
    /// during scans instead of surfacing as unknown clients.
    /// </summary>
    /// <param name="executablePath">The path to the executable file to check.</param>
    /// <returns><c>true</c> when the file is this publisher's non-entry content.</returns>
    bool IsCompanionFile(string executablePath) => false;
}
