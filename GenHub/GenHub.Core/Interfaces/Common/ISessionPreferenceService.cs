namespace GenHub.Core.Interfaces.Common;

/// <summary>
/// Service for managing session-based preferences (reset on app restart).
/// </summary>
public interface ISessionPreferenceService
{
    /// <summary>
    /// Checks if a specific confirmation should be skipped for this session.
    /// </summary>
    /// <param name="key">The unique key for the confirmation.</param>
    /// <returns>True if the confirmation should be skipped; otherwise, false.</returns>
    bool ShouldSkipConfirmation(string key);

    /// <summary>
    /// Sets whether a specific confirmation should be skipped for this session.
    /// </summary>
    /// <param name="key">The unique key for the confirmation.</param>
    /// <param name="skip">Whether to skip the confirmation.</param>
    void SetSkipConfirmation(string key, bool skip);
}
