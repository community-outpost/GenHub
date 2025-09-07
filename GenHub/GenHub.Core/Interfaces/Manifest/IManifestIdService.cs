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
    /// <param name="contentName">The content name.</param>
    /// <param name="manifestSchemaVersion">The manifest schema version.</param>
    /// <returns>A result containing the generated manifest ID or an error.</returns>
    ContentOperationResult<ManifestId> GeneratePublisherContentId(
        string publisherId,
        string contentName,
        string manifestSchemaVersion = "1.0");

    /// <summary>
    /// Generates a manifest ID for a base game installation.
    /// </summary>
    /// <param name="installation">The game installation used to derive the installation segment.</param>
    /// <param name="gameType">The specific game type for the manifest ID.</param>
    /// <param name="manifestSchemaVersion">The manifest schema version.</param>
    /// <returns>A result containing the generated manifest ID or an error.</returns>
    ContentOperationResult<ManifestId> GenerateBaseGameId(
        GameInstallation installation,
        GameType gameType,
        string manifestSchemaVersion = "1.0");

    /// <summary>
    /// Validates a manifest ID string and returns a strongly-typed ManifestId if valid.
    /// </summary>
    /// <param name="manifestIdString">The manifest ID string to validate.</param>
    /// <returns>A result containing the validated manifest ID or an error.</returns>
    ContentOperationResult<ManifestId> ValidateAndCreateManifestId(string manifestIdString);
}
