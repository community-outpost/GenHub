using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Service for generating and validating manifest IDs.
/// </summary>
public class ManifestIdService : IManifestIdService
{
    /// <inheritdoc/>
    public OperationResult<ManifestId> GeneratePublisherContentId(
        string publisherId,
        ContentType contentType,
        string contentName,
        int userVersion = 0)
    {
        if (string.IsNullOrWhiteSpace(publisherId))
            return OperationResult<ManifestId>.CreateFailure("Publisher ID cannot be empty");
        if (string.IsNullOrWhiteSpace(contentName))
            return OperationResult<ManifestId>.CreateFailure("Content name cannot be empty");

        try
        {
            var id = ManifestIdGenerator.GeneratePublisherContentId(publisherId, contentType, contentName, userVersion);
            var manifestId = ManifestId.Create(id);
            return OperationResult<ManifestId>.CreateSuccess(manifestId);
        }
        catch (Exception ex)
        {
            return OperationResult<ManifestId>.CreateFailure($"Failed to generate publisher content ID: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public OperationResult<ManifestId> GenerateGameInstallationId(
        GameInstallation installation,
        GameType gameType,
        object? userVersion)
    {
        if (installation == null)
            return OperationResult<ManifestId>.CreateFailure("Installation cannot be null");

        try
        {
            var id = ManifestIdGenerator.GenerateGameInstallationId(installation, gameType, userVersion);
            var manifestId = ManifestId.Create(id);
            return OperationResult<ManifestId>.CreateSuccess(manifestId);
        }
        catch (Exception ex)
        {
            return OperationResult<ManifestId>.CreateFailure($"Failed to generate game installation ID: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public OperationResult<ManifestId> ValidateAndCreateManifestId(string manifestIdString)
    {
        try
        {
            var manifestId = ManifestId.Create(manifestIdString);
            return OperationResult<ManifestId>.CreateSuccess(manifestId);
        }
        catch (Exception ex)
        {
            return OperationResult<ManifestId>.CreateFailure($"Invalid manifest ID '{manifestIdString}': {ex.Message}");
        }
    }
}
