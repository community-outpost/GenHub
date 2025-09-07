using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using System;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Service for generating and validating manifest IDs using the ResultBase pattern.
/// Provides a clean API for manifest ID operations with proper error handling.
/// </summary>
public class ManifestIdService : IManifestIdService
{
    /// <summary>
    /// Generates a manifest ID for publisher-provided content.
    /// </summary>
    /// <param name="publisherId">Publisher identifier used as the first segment.</param>
    /// <param name="contentName">Human readable content name used as the second segment.</param>
    /// <param name="manifestSchemaVersion">Manifest schema version in phased format.</param>
    /// <returns>A <see cref="ContentOperationResult{ManifestId}"/> containing the generated ID or error details.</returns>
    public ContentOperationResult<ManifestId> GeneratePublisherContentId(
        string publisherId,
        string contentName,
        string manifestSchemaVersion = ManifestConstants.DefaultManifestSchemaVersion)
    {
        try
        {
            var idString = ManifestIdGenerator.GeneratePublisherContentId(publisherId, contentName, manifestSchemaVersion);
            var manifestId = ManifestId.Create(idString);
            return ContentOperationResult<ManifestId>.CreateSuccess(manifestId);
        }
        catch (ArgumentException ex)
        {
            return ContentOperationResult<ManifestId>.CreateFailure(ex.Message);
        }
        catch (Exception)
        {
            return ContentOperationResult<ManifestId>.CreateFailure($"Failed to generate publisher content ID: {publisherId}.{contentName}.{manifestSchemaVersion}");
        }
    }

    /// <summary>
    /// Generates a manifest ID for a base game installation.
    /// </summary>
    /// <param name="installation">The game installation used to derive the installation segment.</param>
    /// <param name="gameType">The specific game type for the manifest ID.</param>
    /// <param name="manifestSchemaVersion">Manifest schema version in phased format.</param>
    /// <returns>A <see cref="ContentOperationResult{ManifestId}"/> containing the generated ID or error details.</returns>
    public ContentOperationResult<ManifestId> GenerateBaseGameId(
        GameInstallation installation,
        GameType gameType,
        string manifestSchemaVersion = ManifestConstants.DefaultManifestSchemaVersion)
    {
        try
        {
            var idString = ManifestIdGenerator.GenerateBaseGameId(installation, gameType, manifestSchemaVersion);
            var manifestId = ManifestId.Create(idString);
            return ContentOperationResult<ManifestId>.CreateSuccess(manifestId);
        }
        catch (ArgumentNullException)
        {
            return ContentOperationResult<ManifestId>.CreateFailure("Installation cannot be null");
        }
        catch (ArgumentException ex)
        {
            return ContentOperationResult<ManifestId>.CreateFailure(ex.Message);
        }
        catch (Exception)
        {
            return ContentOperationResult<ManifestId>.CreateFailure($"Failed to generate base game ID: {installation.InstallationType}.{gameType}.{manifestSchemaVersion}");
        }
    }

    /// <summary>
    /// Validates a manifest ID string and returns a strongly-typed ManifestId if valid.
    /// </summary>
    /// <param name="manifestIdString">The manifest ID string to validate.</param>
    /// <returns>A <see cref="ContentOperationResult{ManifestId}"/> containing the validated ID or error details.</returns>
    public ContentOperationResult<ManifestId> ValidateAndCreateManifestId(string manifestIdString)
    {
        try
        {
            var manifestId = ManifestId.Create(manifestIdString);
            return ContentOperationResult<ManifestId>.CreateSuccess(manifestId);
        }
        catch (ArgumentException ex)
        {
            return ContentOperationResult<ManifestId>.CreateFailure(ex.Message);
        }
        catch (Exception)
        {
            return ContentOperationResult<ManifestId>.CreateFailure($"Failed to validate manifest ID: {manifestIdString}");
        }
    }
}
