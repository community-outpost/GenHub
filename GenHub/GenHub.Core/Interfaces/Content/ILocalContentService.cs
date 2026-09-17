using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Service for creating ContentManifests from local directories.
/// </summary>
public interface ILocalContentService
{
    /// <summary>
    /// Gets the list of allowed content types for local content.
    /// </summary>
    IReadOnlyList<ContentType> AllowedContentTypes { get; }

    /// <summary>
    /// Creates a ContentManifest from a local directory, hashes all files,
    /// and stores the content in the CAS content store.
    /// </summary>
    /// <param name="directoryPath">The path to the local directory containing the content.</param>
    /// <param name="name">The display name of the content.</param>
    /// <param name="contentType">The type of content.</param>
    /// <param name="targetGame">The target game for the content.</param>
    /// <param name="sourcePath">The original source path of the content if known.</param>
    /// <param name="progress">Optional progress reporter for content storage operations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="entryPoint">Optional relative path to the primary executable.</param>
    /// <returns>The created ContentManifest or an error result.</returns>
    Task<OperationResult<ContentManifest>> CreateLocalContentManifestAsync(
        string directoryPath,
        string name,
        ContentType contentType,
        GameType targetGame,
        string? sourcePath = null,
        IProgress<ContentStorageProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string? entryPoint = null);

    /// <summary>
    /// Creates a ContentManifest from a local directory, hashes all files,
    /// and stores the content in the CAS content store with extended options.
    /// </summary>
    /// <param name="directoryPath">The path to the local directory containing the content.</param>
    /// <param name="name">The display name of the content.</param>
    /// <param name="contentType">The type of content.</param>
    /// <param name="targetGame">The target game for the content.</param>
    /// <param name="options">Optional configuration options for the operation.</param>
    /// <returns>The created ContentManifest or an error result.</returns>
    Task<OperationResult<ContentManifest>> CreateLocalContentManifestAsync(
        string directoryPath,
        string name,
        ContentType contentType,
        GameType targetGame,
        LocalContentOptions? options);

    /// <summary>
    /// Convenience method to create and store local content.
    /// </summary>
    /// <param name="name">The display name of the content.</param>
    /// <param name="directoryPath">The path to the directory containing the content.</param>
    /// <param name="contentType">The type of content.</param>
    /// <param name="targetGame">The target game.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created ContentManifest or an error result.</returns>
    Task<OperationResult<ContentManifest>> AddLocalContentAsync(
        string name,
        string directoryPath,
        ContentType contentType,
        GameType targetGame,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes local content by removing its manifest and untracking files from CAS.
    /// </summary>
    /// <param name="manifestId">The ID of the manifest to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success if deleted, failure otherwise.</returns>
    Task<OperationResult> DeleteLocalContentAsync(
        string manifestId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing local content manifest with new contents from a directory.
    /// </summary>
    /// <param name="existingManifestId">The ID of the manifest being updated.</param>
    /// <param name="name">The updated display name.</param>
    /// <param name="directoryPath">The path to the directory with new content.</param>
    /// <param name="contentType">The content type.</param>
    /// <param name="targetGame">The target game.</param>
    /// <param name="sourcePath">The original source path of the content if known.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="entryPoint">Optional relative path to the primary executable.</param>
    /// <returns>The newly created manifest reflecting the updated content.</returns>
    Task<OperationResult<ContentManifest>> UpdateLocalContentManifestAsync(
        string existingManifestId,
        string name,
        string directoryPath,
        ContentType contentType,
        GameType targetGame,
        string? sourcePath = null,
        IProgress<ContentStorageProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string? entryPoint = null);

    /// <summary>
    /// Updates an existing local content manifest with new contents from a directory using extended options.
    /// </summary>
    /// <param name="existingManifestId">The ID of the manifest being updated.</param>
    /// <param name="name">The updated display name.</param>
    /// <param name="directoryPath">The path to the directory with new content.</param>
    /// <param name="contentType">The content type.</param>
    /// <param name="targetGame">The target game.</param>
    /// <param name="options">Optional configuration options for the operation.</param>
    /// <returns>The newly created manifest reflecting the updated content.</returns>
    Task<OperationResult<ContentManifest>> UpdateLocalContentManifestAsync(
        string existingManifestId,
        string name,
        string directoryPath,
        ContentType contentType,
        GameType targetGame,
        LocalContentOptions? options);
}
