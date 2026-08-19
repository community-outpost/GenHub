namespace GenHub.Core.Constants;

/// <summary>
/// Constants for command line arguments and the <c>genhub://</c> URI scheme.
/// </summary>
/// <remarks>
/// Subscription links use <c>genhub://subscribe?url=&lt;absolute-url&gt;</c>.
/// Today <c>url</c> is a hosted GenHub <c>catalog.json</c>. Publisher Studio will also share
/// Provider Definition URLs via the same scheme; GenHub will detect payload type at fetch time.
/// </remarks>
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
    /// <summary>
    /// Scheme name for custom protocol registration.
    /// </summary>
    public const string SchemeName = "genhub";

    /// <summary>
    /// Custom URI scheme registered so OS/browser links can open GenHub.
    /// </summary>
    public const string UriScheme = SchemeName + "://";

    /// <summary>
    /// URI path segment for content subscription (<c>genhub://subscribe?url=...</c>).
    /// </summary>
    public const string SubscribeCommand = "subscribe";

    /// <summary>
    /// Full prefix for subscription URIs (<c>genhub://subscribe</c>).
    /// </summary>
    public const string SubscribeUriPrefix = UriScheme + SubscribeCommand;

    /// <summary>
    /// Query parameter carrying the absolute URL of a catalog (or future provider definition).
    /// </summary>
    public const string SubscribeUrlParam = "?url=";
}
