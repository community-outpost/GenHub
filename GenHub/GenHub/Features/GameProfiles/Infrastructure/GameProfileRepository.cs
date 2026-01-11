using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        Converters = { new JsonStringEnumConverter() },
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

            // Ensure the profiles directory exists
            EnsureDirectoryExists();

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
            // If the profiles directory doesn't exist, return an empty list
            if (!Directory.Exists(_profilesDirectory))
            {
                _logger.LogDebug("Profiles directory {Directory} does not exist. Returning empty profile list.", _profilesDirectory);
                return ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess(new List<GameProfile>().AsReadOnly());
            }

            var profileFiles = Directory.GetFiles(_profilesDirectory, "*.json");
            var profiles = new List<GameProfile>();

            foreach (var filePath in profileFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(filePath, cancellationToken);

                    if (string.IsNullOrWhiteSpace(json))
                    {
                        _logger.LogWarning("Profile file {FilePath} is empty. Renaming to .corrupted", filePath);
                        TryQuarantineCorruptedFile(filePath);
                        continue;
                    }

                    var profile = JsonSerializer.Deserialize<GameProfile>(json, _jsonOptions);
                    if (profile != null)
                    {
                        profiles.Add(profile);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize profile from {FilePath}. File may be corrupted. Renaming to .corrupted", filePath);
                    TryQuarantineCorruptedFile(filePath);
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

    private void TryQuarantineCorruptedFile(string filePath)
    {
        try
        {
            var corruptedPath = filePath + ".corrupted";
            if (File.Exists(corruptedPath))
            {
                File.Delete(corruptedPath);
            }

            File.Move(filePath, corruptedPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to quarantine corrupted profile file {FilePath}", filePath);
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

    /// <summary>
    /// Ensures profiles directory exists.
    /// </summary>
    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(_profilesDirectory))
        {
            Directory.CreateDirectory(_profilesDirectory);
            _logger.LogDebug("Created profiles directory: {Directory}", _profilesDirectory);
        }
    }
}
