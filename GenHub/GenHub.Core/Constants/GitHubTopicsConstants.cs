namespace GenHub.Core.Constants;

/// <summary>
/// Constants for GitHub Topics discovery functionality.
/// </summary>
public static class GitHubTopicsConstants
{
    /// <summary>
    /// The main topic used to identify GenHub-compatible repositories.
    /// </summary>
    public const string GenHubTopic = "genhub";

    /// <summary>
    /// Topic for Generals Online related content.
    /// </summary>
    public const string GeneralsOnlineTopic = "generalsonline";

    /// <summary>
    /// Topic for Generals game modifications.
    /// </summary>
    public const string GeneralsModTopic = "generals-mod";

    /// <summary>
    /// Topic for Zero Hour game modifications.
    /// </summary>
    public const string ZeroHourModTopic = "zero-hour-mod";

    /// <summary>
    /// Topic for game client content.
    /// </summary>
    public const string GameClientTopic = "game-client";

    /// <summary>
    /// Topic for mod content.
    /// </summary>
    public const string ModTopic = "mod";

    /// <summary>
    /// Topic for map pack content.
    /// </summary>
    public const string MapPackTopic = "map-pack";

    /// <summary>
    /// Topic for addon content.
    /// </summary>
    public const string AddonTopic = "addon";

    /// <summary>
    /// Topic for patch content.
    /// </summary>
    public const string PatchTopic = "patch";

    /// <summary>
    /// Topic for language pack content.
    /// </summary>
    public const string LanguagePackTopic = "language-pack";

    /// <summary>
    /// Topic for mission content.
    /// </summary>
    public const string MissionTopic = "mission";

    /// <summary>
    /// Topic for map content.
    /// </summary>
    public const string MapTopic = "map";

    /// <summary>
    /// Default number of results per page for GitHub API searches.
    /// </summary>
    public const int DefaultPerPage = 30;

    /// <summary>
    /// Cache duration for search results in minutes.
    /// </summary>
    public const int CacheDurationMinutes = 10;

    /// <summary>
    /// Display name for the GitHub Topics publisher.
    /// </summary>
    public const string PublisherName = "GitHub";

    /// <summary>
    /// Publisher type identifier for GitHub Topics.
    /// </summary>
    public const string PublisherType = "github-topics";

    /// <summary>
    /// Publisher logo source path for UI display.
    /// </summary>
    public const string LogoSource = "avares://GenHub/Assets/Logos/github-logo.png";

    /// <summary>
    /// Description for the GitHub Topics discoverer.
    /// </summary>
    public const string DiscovererSourceName = "GitHub";

    /// <summary>
    /// Description for the GitHub Topics discoverer.
    /// </summary>
    public const string DiscovererDescription = "Discovers community content from GitHub repositories tagged with GenHub topics";

    /// <summary>
    /// Description for the GitHub Topics provider.
    /// </summary>
    public const string ProviderDescription = "Community repositories tagged with GenHub topics";

    /// <summary>
    /// Metadata key for the source topic that discovered the repository.
    /// </summary>
    public const string SourceTopicMetadataKey = "source-topic";

    /// <summary>
    /// Metadata key for star count.
    /// </summary>
    public const string StarCountMetadataKey = "star-count";

    /// <summary>
    /// Metadata key for fork count.
    /// </summary>
    public const string ForkCountMetadataKey = "fork-count";

    /// <summary>
    /// Metadata key for primary language.
    /// </summary>
    public const string LanguageMetadataKey = "language";
}