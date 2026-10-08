using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Represents a user's subscription status to a content publisher.
/// This enables users to receive update notifications only from publishers they care about.
/// </summary>
public class PublisherSubscription
{
    /// <summary>
    /// Gets or sets the unique identifier for the publisher (e.g., "generals-online", "community-outpost", "local").
    /// </summary>
    public string PublisherId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name of the publisher.
    /// </summary>
    public string PublisherName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the user is subscribed to receive updates from this publisher.
    /// When false, the user will not receive update notifications from this publisher.
    /// </summary>
    public bool IsSubscribed { get; set; } = true;

    /// <summary>
    /// Gets or sets the date and time when the subscription was created.
    /// </summary>
    public DateTime SubscribedDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the date and time when the subscription was last updated.
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the specific version that the user chose to skip.
    /// When set, this specific version will not be prompted again,
    /// but newer versions from this publisher will still be shown.
    /// This is different from unsubscribing (IsSubscribed = false).
    /// </summary>
    public string? SkippedVersion { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the current version was skipped.
    /// </summary>
    public DateTime? SkippedVersionDate { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user has chosen to be notified about all updates
    /// from this publisher without prompting (auto-update enabled).
    /// When true, updates are applied automatically based on the user's preferred strategy.
    /// </summary>
    public bool AutoUpdateEnabled { get; set; }

    /// <summary>
    /// Gets or sets the user's preferred update strategy for this publisher.
    /// This allows per-publisher customization of how updates are applied.
    /// </summary>
    public UpdateStrategy? PreferredUpdateStrategy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to delete old versions when updating.
    /// This allows per-publisher customization of cleanup behavior.
    /// </summary>
    public bool? DeleteOldVersions { get; set; } = true;

    /// <summary>
    /// Gets or sets the last version that was successfully installed for this publisher.
    /// Used to track update history and determine if an update is available.
    /// </summary>
    public string? LastInstalledVersion { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the last version was installed.
    /// </summary>
    public DateTime? LastInstalledDate { get; set; }

    /// <summary>
    /// Gets a value indicating whether this subscription is currently active.
    /// </summary>
    public bool IsActive => IsSubscribed;

    /// <summary>
    /// Gets a value indicating whether there's a pending skipped version
    /// that should be cleared when a newer version becomes available.
    /// </summary>
    public bool HasSkippedVersion => !string.IsNullOrEmpty(SkippedVersion);

    /// <summary>
    /// Clears the skipped version, typically called when a newer version than
    /// the skipped one becomes available.
    /// </summary>
    public void ClearSkippedVersion()
    {
        SkippedVersion = null;
        SkippedVersionDate = null;
        LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks a specific version as skipped.
    /// </summary>
    /// <param name="version">The version to skip.</param>
    public void SkipVersion(string version)
    {
        SkippedVersion = version;
        SkippedVersionDate = DateTime.UtcNow;
        LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Records that a version was successfully installed.
    /// </summary>
    /// <param name="version">The version that was installed.</param>
    public void RecordInstallation(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        LastInstalledVersion = version;
        LastInstalledDate = DateTime.UtcNow;
        LastUpdated = DateTime.UtcNow;

        // Clear skipped version when installing any version.
        // (either this version or a newer one).
        ClearSkippedVersion();
    }

    /// <summary>
    /// Checks if a given version should be skipped.
    /// </summary>
    /// <param name="version">The version to check.</param>
    /// <param name="versionComparer">Optional version comparer for semantic version comparison.</param>
    /// <returns>True if the version should be skipped; otherwise, false.</returns>
    public bool ShouldSkipVersion(string version, IComparer<string>? versionComparer = null)
    {
        if (string.IsNullOrEmpty(SkippedVersion))
        {
            return false;
        }

        // If the versions are the same, skip it.
        if (string.Equals(version, SkippedVersion, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // If a version comparer is provided, check if the new version is newer than the skipped one.
        if (versionComparer != null)
        {
            try
            {
                // If the new version is newer than the skipped version, don't skip it.
                var comparison = versionComparer.Compare(version, SkippedVersion);
                if (comparison > 0)
                {
                    return false; // Newer version, should not be skipped.
                }

                // If comparison is 0 (equal) or < 0 (older), skip it.
                return true;
            }
            catch (Exception ex)
            {
                // If comparison fails, fall through to default behavior.
                System.Diagnostics.Debug.WriteLine($"Version comparison failed: {ex.Message}");
            }
        }

        // Default behavior: only skip the exact version that was skipped.
        // Since we already checked for exact match above, if we get here the versions are different.
        return false;
    }

    /// <summary>
    /// Creates a deep copy of this PublisherSubscription instance.
    /// </summary>
    /// <returns>A new PublisherSubscription with all properties copied.</returns>
    public PublisherSubscription Clone()
    {
        return new PublisherSubscription
        {
            PublisherId = PublisherId,
            PublisherName = PublisherName,
            IsSubscribed = IsSubscribed,
            SubscribedDate = SubscribedDate,
            LastUpdated = LastUpdated,
            SkippedVersion = SkippedVersion,
            SkippedVersionDate = SkippedVersionDate,
            AutoUpdateEnabled = AutoUpdateEnabled,
            PreferredUpdateStrategy = PreferredUpdateStrategy,
            DeleteOldVersions = DeleteOldVersions,
            LastInstalledVersion = LastInstalledVersion,
            LastInstalledDate = LastInstalledDate,
        };
    }
}
