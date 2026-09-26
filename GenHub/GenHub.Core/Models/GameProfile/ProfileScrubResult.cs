using System.Collections.Generic;

namespace GenHub.Core.Models.GameProfile;

/// <summary>
/// Represents the result of scrubbing deleted manifest references from game profiles.
/// </summary>
/// <param name="UpdatedProfilesCount">The number of profiles whose content lists were updated.</param>
/// <param name="DeletedProfilesCount">The number of orphaned profiles that were deleted.</param>
/// <param name="FailedProfileNames">The names of profiles that could not be updated or deleted.</param>
public record ProfileScrubResult(
    int UpdatedProfilesCount,
    int DeletedProfilesCount,
    IReadOnlyList<string> FailedProfileNames);
