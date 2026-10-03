using System.Collections.Generic;

namespace GenHub.Core.Models.GameProfile;

/// <summary>
/// Describes the effective file set used to verify a game profile: the manifest-expected
/// base files plus locally resolved profile overlay archives.
/// </summary>
/// <param name="AllowedBaseRelativePaths">
/// Game-root-relative file paths named by the profile's installation, client, and enabled
/// content manifests, or <c>null</c> when no manifest could be resolved (scan the folder unfiltered).
/// </param>
/// <param name="OverlayModPaths">
/// Absolute local paths of enabled content archives to mount with top override priority,
/// ordered by ascending content priority so higher-priority content wins ties.
/// </param>
/// <param name="IsComplete">
/// <c>false</c> when an enabled overlay archive could not be resolved locally, meaning the
/// calculated CRC cannot be trusted.
/// </param>
public sealed record ProfileVerificationFileSet(
    IReadOnlyCollection<string>? AllowedBaseRelativePaths,
    IReadOnlyList<string> OverlayModPaths,
    bool IsComplete);
