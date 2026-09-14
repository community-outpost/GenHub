using System;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// Option model representing an installed game target in the dropdown.
/// </summary>
public sealed class SharedInstallationOption : IEquatable<SharedInstallationOption>
{
    /// <summary>
    /// Gets the unique identifier of the installation.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the display name for the UI dropdown.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the directory path of the installation.
    /// </summary>
    public required string InstallationPath { get; init; }

    /// <inheritdoc/>
    public bool Equals(SharedInstallationOption? other) => other != null && Id == other.Id;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is SharedInstallationOption other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Id.GetHashCode(StringComparison.Ordinal);
}
