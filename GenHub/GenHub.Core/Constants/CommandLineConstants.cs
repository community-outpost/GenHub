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

    /// <summary>
    /// URI path segment for profile commands (<c>genhub://profile/...</c>).
    /// </summary>
    public const string ProfileCommand = "profile";

    /// <summary>
    /// URI subcommand for profile import (<c>genhub://profile/import</c>).
    /// </summary>
    public const string ProfileImportSubcommand = "import";

    /// <summary>
    /// URI subcommand for profile view (<c>genhub://profile/view</c>).
    /// </summary>
    public const string ProfileViewSubcommand = "view";

    /// <summary>
    /// Full prefix for profile import URIs (<c>genhub://profile/import</c>).
    /// </summary>
    public const string ProfileImportUriPrefix = UriScheme + "profile/import";

    /// <summary>
    /// Full prefix for profile view URIs (<c>genhub://profile/view</c>).
    /// </summary>
    public const string ProfileViewUriPrefix = UriScheme + "profile/view";

    /// <summary>
    /// Query parameter name carrying inline compressed profile data.
    /// </summary>
    public const string DataQueryParam = "data=";

    /// <summary>
    /// Query parameter name carrying remote profile URL.
    /// </summary>
    public const string UrlQueryParam = "url=";

    /// <summary>
    /// Query parameter name carrying catalog profile ID.
    /// </summary>
    public const string IdQueryParam = "id=";

    /// <summary>
    /// Query parameter name carrying catalog publisher identifier.
    /// </summary>
    public const string PublisherQueryParam = "publisher=";

    /// <summary>
    /// Command-line argument used to request importing a shared profile.
    /// </summary>
    public const string ImportProfileArg = "--import-profile";

    /// <summary>
    /// Command-line argument prefix for inline shared profile importing.
    /// </summary>
    public const string ImportProfileInlinePrefix = "--import-profile=";

    /// <summary>
    /// Command-line argument used to allow running multiple instances concurrently.
    /// </summary>
    public const string MultiInstanceArg = "--multi-instance";

    /// <summary>
    /// Short command-line argument used to allow running multiple instances concurrently.
    /// </summary>
    public const string MultiInstanceShortArg = "-m";

    /// <summary>
    /// Environment variable name to allow running multiple instances concurrently.
    /// </summary>
    public const string MultiInstanceEnvVar = "GENHUB_MULTI_INSTANCE";

    /// <summary>
    /// Environment variable value representing enabled multi-instance mode.
    /// </summary>
    public const string MultiInstanceEnvEnabledValue = "1";
}
