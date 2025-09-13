using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.Infrastructure;

/// <summary>
/// File-based implementation of the game profile repository.
/// </summary>
public class GameProfileRepository(
    string profilesDirectory,
    ILogger<GameProfileRepository> logger) : IGameProfileRepository
{
    private readonly string _profilesDirectory = profilesDirectory ?? throw new ArgumentNullException(nameof(profilesDirectory));
    private readonly ILogger<GameProfileRepository> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<GameProfile>> SaveProfileAsync(GameProfile profile, CancellationToken cancellationToken = default)
    {
        try
        {
            if (profile == null)
                return ProfileOperationResult<GameProfile>.CreateFailure("Profile cannot be null");

            // Generate new ID if not set
            if (string.IsNullOrEmpty(profile.Id))
            {
                profile.Id = Guid.NewGuid().ToString();
            }

            var filePath = GetProfileFilePath(profile.Id);
            var json = JsonSerializer.Serialize(profile, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json, cancellationToken);

            _logger.LogInformation("Successfully saved profile {ProfileId} to {FilePath}", profile.Id, filePath);
            return ProfileOperationResult<GameProfile>.CreateSuccess(profile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save profile {ProfileId}", profile?.Id);
            return ProfileOperationResult<GameProfile>.CreateFailure($"Failed to save profile: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<GameProfile>> LoadProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(profileId))
                return ProfileOperationResult<GameProfile>.CreateFailure("Profile ID cannot be null or empty");

            var filePath = GetProfileFilePath(profileId);
            if (!File.Exists(filePath))
            {
                return ProfileOperationResult<GameProfile>.CreateFailure($"Profile not found: {profileId}");
            }

            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            var profile = JsonSerializer.Deserialize<GameProfile>(json, _jsonOptions);

            if (profile == null)
            {
                return ProfileOperationResult<GameProfile>.CreateFailure($"Failed to deserialize profile: {profileId}");
            }

            _logger.LogDebug("Successfully loaded profile {ProfileId}", profileId);
            return ProfileOperationResult<GameProfile>.CreateSuccess(profile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load profile {ProfileId}", profileId);
            return ProfileOperationResult<GameProfile>.CreateFailure($"Failed to load profile: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<IReadOnlyList<GameProfile>>> LoadAllProfilesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var profileFiles = Directory.GetFiles(_profilesDirectory, "*.json");
            var profiles = new List<GameProfile>();

            foreach (var filePath in profileFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(filePath, cancellationToken);
                    var profile = JsonSerializer.Deserialize<GameProfile>(json, _jsonOptions);
                    if (profile != null)
                    {
                        profiles.Add(profile);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load profile from {FilePath}", filePath);
                }
            }

            _logger.LogDebug("Successfully loaded {Count} profiles", profiles.Count);
            return ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess(profiles.AsReadOnly());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load all profiles");
            return ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateFailure($"Failed to load profiles: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<GameProfile>> DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(profileId))
                return ProfileOperationResult<GameProfile>.CreateFailure("Profile ID cannot be null or empty");

            var filePath = GetProfileFilePath(profileId);
            if (!File.Exists(filePath))
            {
                return ProfileOperationResult<GameProfile>.CreateFailure($"Profile not found: {profileId}");
            }

            // Load the profile before deleting for return value
            var loadResult = await LoadProfileAsync(profileId, cancellationToken);
            if (loadResult.Failed)
            {
                return loadResult;
            }

            File.Delete(filePath);
            _logger.LogInformation("Successfully deleted profile {ProfileId}", profileId);
            return ProfileOperationResult<GameProfile>.CreateSuccess(loadResult.Data!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete profile {ProfileId}", profileId);
            return ProfileOperationResult<GameProfile>.CreateFailure($"Failed to delete profile: {ex.Message}");
        }
    }

    private string GetProfileFilePath(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            throw new ArgumentException("Profile ID cannot be null or empty", nameof(profileId));

        // Validate profileId contains only valid filename characters
        if (profileId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Profile ID contains invalid characters", nameof(profileId));

        return Path.Combine(_profilesDirectory, $"{profileId}.json");
    }
}
