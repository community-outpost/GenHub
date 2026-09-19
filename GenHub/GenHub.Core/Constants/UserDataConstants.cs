namespace GenHub.Core.Constants;

/// <summary>
/// Constants for tracked user data installations.
/// </summary>
public static class UserDataConstants
{
    /// <summary>
    /// Suffix appended to a deployed file that no longer matches its recorded hash when it is
    /// moved aside so the pristine backup can be restored over it.
    /// </summary>
    public const string UserModifiedSuffix = ".user-modified";
}
