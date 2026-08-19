using System;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.GameProfiles;

/// <summary>
/// Service responsible for exporting, inspecting, and importing shared game profile packages.
/// </summary>
public interface IProfileSharingService
{
    /// <summary>
    /// Generates a compact <c>genhub://profile/import?data=...</c> sharing URI for a given profile.
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile to share.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An operation result containing the generated URI.</returns>
    Task<OperationResult<string>> ExportProfileToUriAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports a self-contained <c>.ghprofile</c> JSON container file for a given profile.
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile to share.</param>
    /// <param name="destinationPath">The file path to save the package to.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An operation result containing the destination file path.</returns>
    Task<OperationResult<string>> ExportProfileToFileAsync(string profileId, string destinationPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports the raw serialized JSON string for a given profile package.
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile to share.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An operation result containing the JSON string.</returns>
    Task<OperationResult<string>> ExportProfileToJsonAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses and inspects a share URI, JSON package, or <c>.ghprofile</c> file, performing local CAS/manifest diffing without modifying state.
    /// </summary>
    /// <param name="shareUriOrJsonOrPath">The share URI, raw JSON, or file path to inspect.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An operation result containing detailed inspection and compatibility information.</returns>
    Task<OperationResult<SharedProfileInspectionResult>> InspectSharedProfileAsync(string shareUriOrJsonOrPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires missing dependencies and installs the shared profile.
    /// </summary>
    /// <param name="request">The import request with confirmed settings and installation target.</param>
    /// <param name="progress">Optional progress reporter for multi-phase acquisition.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An operation result containing the newly created profile.</returns>
    Task<OperationResult<GameProfile>> ImportSharedProfileAsync(
        SharedProfileImportRequest request,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a formatted Discord markdown message invite for a profile.
    /// </summary>
    /// <param name="profile">The profile being shared.</param>
    /// <param name="shareUri">The generated share URI.</param>
    /// <returns>The formatted markdown string.</returns>
    string GenerateDiscordMarkdown(GameProfile profile, string shareUri);
}
