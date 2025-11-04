using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Manifest;

/// <summary>
/// Defines the contract for manifest ID generation and validation services.
/// Provides methods for creating deterministic, human-readable manifest identifiers
/// with error handling through the ResultBase.
/// </summary>
public interface IManifestIdService
{
    /// <summary>
    /// Generates a manifest ID for publisher-provided content.
    /// </summary>
    /// <param name="publisherId">The publisher identifier.</param>
    /// <param name="contentType">The type of content being identified.</param>
    /// <param name="contentName">The content name.</param>
    /// <param name="userVersion">User-specified version number (e.g., 1, 2, 20). Defaults to 0 for first version.</param>
    /// <returns>A result containing the generated manifest ID or an error.</returns>
    OperationResult<ManifestId> GeneratePublisherContentId(
        string publisherId,
        ContentType contentType,
        string contentName,
        int userVersion = 0);

    /// <summary>
    /// Generates a manifest ID for a game installation with string version normalization.
    /// </summary>
    /// <param name="installation">The game installation used to derive the installation segment.</param>
    /// <param name="gameType">The specific game type for the manifest ID.</param>
    /// <param name="userVersion">User-specified version (e.g., "1.08", "1.04", or integer like 0, 1, 2). If null, defaults to 0.</param>
    /// <returns>A result containing the generated manifest ID or an error.</returns>
    OperationResult<ManifestId> GenerateGameInstallationId(
        GameInstallation installation,
        GameType gameType,
        object? userVersion);

    /// <summary>
    /// Validates a manifest ID string and returns a strongly-typed ManifestId if valid.
    /// </summary>
    /// <param name="manifestIdString">The manifest ID string to validate.</param>
    /// <returns>A result containing the validated manifest ID or an error.</returns>
    OperationResult<ManifestId> ValidateAndCreateManifestId(string manifestIdString);
}
