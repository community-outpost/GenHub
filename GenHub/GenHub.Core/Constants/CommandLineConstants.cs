namespace GenHub.Core.Constants;

/// <summary>
/// Constants for command line arguments and URI schemes.
/// </summary>
public static class CommandLineConstants
{
    /// <summary>
    /// Command-line argument used to request launching a profile.
    /// </summary>
    public const string LaunchProfileArg = "--launch-profile";

    /// <summary>
    /// Command-line argument prefix for inline profile launching.
    /// </summary>
    public const string LaunchProfileInlinePrefix = "--launch-profile=";

    /// <summary>
    /// URI scheme used for protocol handling.
    /// </summary>
    public const string UriScheme = "genhub://";

    /// <summary>
    /// Command for subscribing to a catalog via URI.
    /// </summary>
    public const string SubscribeCommand = "subscribe";

    /// <summary>
    /// Full prefix for subscription URI.
    /// </summary>
    public const string SubscribeUriPrefix = UriScheme + SubscribeCommand;

    /// <summary>
    /// Query parameter name for the catalog URL in a subscription URI.
    /// </summary>
    public const string SubscribeUrlParam = "?url=";
}
