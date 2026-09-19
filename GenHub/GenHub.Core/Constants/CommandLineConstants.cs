using System.Diagnostics.CodeAnalysis;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants for command line arguments and the <c>genhub://</c> URI scheme.
/// </summary>
/// <remarks>
/// Subscription links use <c>genhub://subscribe?url=&lt;absolute-url&gt;</c>.<br/>
/// Today <c>url</c> is a hosted GenHub <c>catalog.json</c>. Publisher Studio will also share
/// Provider Definition URLs via the same scheme; GenHub will detect payload type at fetch time.<br/>
/// Tool share links use <c>genhub://map/import?url=&lt;absolute-url&gt;</c> and
/// <c>genhub://replay/import?url=&lt;absolute-url&gt;</c> with an optional <c>game</c> query value.
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
    /// Query parameter key carrying remote profile URL.
    /// </summary>
    public const string UrlQueryKey = "url";

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
    public const string SubscribeUrlParam = "?" + UrlQueryKey + "=";

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
    [SuppressMessage("Minor Code Smell", "S1075:URIs and paths should not be hardcoded", Justification = "Custom URI scheme format, not a filesystem path")]
    public const string ProfileImportUriPrefix = $"{UriScheme}{ProfileCommand}/{ProfileImportSubcommand}";

    /// <summary>
    /// Full prefix for profile view URIs (<c>genhub://profile/view</c>).
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S1075:URIs and paths should not be hardcoded", Justification = "Custom URI scheme format, not a filesystem path")]
    public const string ProfileViewUriPrefix = $"{UriScheme}{ProfileCommand}/{ProfileViewSubcommand}";

    /// <summary>
    /// Query parameter key carrying inline compressed profile data.
    /// </summary>
    public const string DataQueryKey = "data";

    /// <summary>
    /// Query parameter key carrying catalog profile ID.
    /// </summary>
    public const string IdQueryKey = "id";

    /// <summary>
    /// Query parameter key carrying catalog publisher identifier.
    /// </summary>
    public const string PublisherQueryKey = "publisher";

    /// <summary>
    /// Query parameter name carrying inline compressed profile data.
    /// </summary>
    public const string DataQueryParam = DataQueryKey + "=";

    /// <summary>
    /// Query parameter name carrying remote profile URL.
    /// </summary>
    public const string UrlQueryParam = UrlQueryKey + "=";

    /// <summary>
    /// Query parameter name carrying catalog profile ID.
    /// </summary>
    public const string IdQueryParam = IdQueryKey + "=";

    /// <summary>
    /// Query parameter name carrying catalog publisher identifier.
    /// </summary>
    public const string PublisherQueryParam = PublisherQueryKey + "=";

    /// <summary>
    /// URI path segment for map share links (<c>genhub://map/import?url=...</c>).
    /// </summary>
    public const string MapCommand = "map";

    /// <summary>
    /// URI path segment for replay share links (<c>genhub://replay/import?url=...</c>).
    /// </summary>
    public const string ReplayCommand = "replay";

    /// <summary>
    /// URI subcommand for tool imports (<c>genhub://map/import</c>, <c>genhub://replay/import</c>).
    /// </summary>
    public const string ToolImportSubcommand = "import";

    /// <summary>
    /// Full prefix for map import URIs (<c>genhub://map/import</c>).
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S1075:URIs and paths should not be hardcoded", Justification = "Custom URI scheme format, not a filesystem path")]
    public const string MapImportUriPrefix = $"{UriScheme}{MapCommand}/{ToolImportSubcommand}";

    /// <summary>
    /// Full prefix for replay import URIs (<c>genhub://replay/import</c>).
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S1075:URIs and paths should not be hardcoded", Justification = "Custom URI scheme format, not a filesystem path")]
    public const string ReplayImportUriPrefix = $"{UriScheme}{ReplayCommand}/{ToolImportSubcommand}";

    /// <summary>
    /// Query parameter key carrying the target game for tool imports.
    /// </summary>
    public const string GameQueryKey = "game";

    /// <summary>
    /// Query parameter name carrying the target game for tool imports.
    /// </summary>
    public const string GameQueryParam = GameQueryKey + "=";

    /// <summary>
    /// Query parameter value targeting Command and Conquer: Generals.
    /// </summary>
    public const string GameGeneralsValue = "generals";

    /// <summary>
    /// Query parameter value targeting Command and Conquer: Generals Zero Hour.
    /// </summary>
    public const string GameZeroHourValue = "zerohour";

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

    /// <summary>
    /// Environment variable set by AppImage runtimes pointing to the mounted AppImage file.
    /// </summary>
    public const string AppImageEnvVar = "APPIMAGE";

    /// <summary>
    /// Prefix used for single-instance inter-process communication pipe names.
    /// </summary>
    public const string SingleInstancePipePrefix = "GenHub_";

    /// <summary>
    /// Suffix used for single-instance inter-process communication pipe names.
    /// </summary>
    public const string SingleInstancePipeSuffix = "SingleInstance_Pipe";

    /// <summary>
    /// Maximum retry attempts to forward command-line arguments to an existing primary instance.
    /// </summary>
    public const int SingleInstanceMaxForwardAttempts = 3;
}
