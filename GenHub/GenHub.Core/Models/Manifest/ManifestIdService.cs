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
    /// <param name="userVersion">User-specified version number (e.g., 1, 2, 20). Defaults to 0 for first version.</param>
    /// <returns>A <see cref="OperationResult{ManifestId}"/> containing the generated ID or error details.</returns>
    public OperationResult<ManifestId> GeneratePublisherContentId(
        string publisherId,
        string contentName,
        int userVersion = 0)
    {
        try
        {
            var idString = ManifestIdGenerator.GeneratePublisherContentId(publisherId, contentName, userVersion);
            var manifestId = ManifestId.Create(idString);
            return OperationResult<ManifestId>.CreateSuccess(manifestId);
        }
        catch (ArgumentException ex)
        {
            return OperationResult<ManifestId>.CreateFailure(ex.Message);
        }
        catch (Exception)
        {
            return OperationResult<ManifestId>.CreateFailure($"Failed to generate publisher content ID: {publisherId}.{contentName}.{userVersion}");
        }
    }

    /// <summary>
    /// Generates a manifest ID for a game installation.
    /// </summary>
    /// <param name="installation">The game installation used to derive the installation segment.</param>
    /// <param name="gameType">The specific game type for the manifest ID.</param>
    /// <param name="userVersion">User-specified version number (e.g., 1, 2, 20). Defaults to 0 for first version.</param>
    /// <returns>A <see cref="OperationResult{ManifestId}"/> containing the generated ID or error details.</returns>
    public OperationResult<ManifestId> GenerateGameInstallationId(
        GameInstallation installation,
        GameType gameType,
        int userVersion = 0)
    {
        try
        {
            var idString = ManifestIdGenerator.GenerateGameInstallationId(installation, gameType, userVersion);
            var manifestId = ManifestId.Create(idString);
            return OperationResult<ManifestId>.CreateSuccess(manifestId);
        }
        catch (ArgumentNullException)
        {
            return OperationResult<ManifestId>.CreateFailure("Installation cannot be null");
        }
        catch (ArgumentException ex)
        {
            return OperationResult<ManifestId>.CreateFailure(ex.Message);
        }
        catch (Exception)
        {
            return OperationResult<ManifestId>.CreateFailure($"Failed to generate game installation ID: {installation.InstallationType}.{gameType}.{userVersion}");
        }
    }

    /// <summary>
    /// Validates a manifest ID string and returns a strongly-typed ManifestId if valid.
    /// </summary>
    /// <param name="manifestIdString">The manifest ID string to validate.</param>
    /// <returns>A <see cref="OperationResult{ManifestId}"/> containing the validated ID or error details.</returns>
    public OperationResult<ManifestId> ValidateAndCreateManifestId(string manifestIdString)
    {
        try
        {
            var manifestId = ManifestId.Create(manifestIdString);
            return OperationResult<ManifestId>.CreateSuccess(manifestId);
        }
        catch (ArgumentException ex)
        {
            return OperationResult<ManifestId>.CreateFailure(ex.Message);
        }
        catch (Exception)
        {
            return OperationResult<ManifestId>.CreateFailure($"Failed to validate manifest ID: {manifestIdString}");
        }
    }
}
