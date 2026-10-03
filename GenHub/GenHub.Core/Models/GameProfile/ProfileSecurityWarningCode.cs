namespace GenHub.Core.Models.GameProfile;

/// <summary>
/// Identifies security or sanitization warnings detected in a shared game profile.
/// </summary>
public enum ProfileSecurityWarningCode
{
    /// <summary>
    /// Dangerous shell or command chaining characters were removed from launch arguments.
    /// </summary>
    SanitizedShellCharacters,

    /// <summary>
    /// Quotes or percent signs were removed from launch arguments.
    /// </summary>
    SanitizedQuotesOrPercent,

    /// <summary>
    /// Control characters were removed from launch arguments.
    /// </summary>
    SanitizedControlCharacters,

    /// <summary>
    /// A missing dependency has no download URL or package URL source.
    /// </summary>
    MissingDownloadSource,
}
