namespace GenHub.Core.Constants;

/// <summary>
/// URI scheme constants for handling different types of URIs and paths.
/// </summary>
public static class UriConstants
{
    /// <summary>
    /// URI scheme for Avalonia embedded resources.
    /// </summary>
    public const string AvarUriScheme = "avares://";

    /// <summary>
    /// HTTP URI scheme.
    /// </summary>
    public const string HttpUriScheme = "http://";

    /// <summary>
    /// HTTPS URI scheme.
    /// </summary>
    public const string HttpsUriScheme = "https://";

    /// <summary>
    /// Icon URI for Generals game type.
    /// </summary>
    public const string GeneralsIconUri = "avares://GenHub/Assets/Icons/generals-icon.png";

    /// <summary>
    /// Icon URI for Zero Hour game type.
    /// </summary>
    public const string ZeroHourIconUri = "avares://GenHub/Assets/Icons/zerohour-icon.png";

    /// <summary>
    /// Default icon URI for unknown game types.
    /// </summary>
    public const string DefaultIconUri = "avares://GenHub/Assets/Icons/generalshub-icon.png";
}
