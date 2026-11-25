namespace GenHub.Core.Constants;

/// <summary>
/// Regex pattern constants.
/// </summary>
public static class RegexConstants
{
    /// <summary>
    /// Regex pattern for Generals Online replay URLs.
    /// </summary>
    public const string GeneralsOnlineReplayPattern = @"https://matchdata\.playgenerals\.online/[^""]+_replay\.rep";

    /// <summary>
    /// Regex pattern for GenTool replay links.
    /// </summary>
    public const string GenToolReplayPattern = @"href=""([^\""]+\.rep)""";

    /// <summary>
    /// Regex pattern for Strata / GameReplays replay links.
    /// </summary>
    public const string StrataReplayPattern = @"(?:href=[""'](?<url>[^""']+\.(?:rep|zip))[""']|(?<url>https?://[^""'\s<>]+\.(?:rep|zip)))";

    /// <summary>
    /// Publisher ID grammar: lowercase letters, digits, and hyphens.
    /// </summary>
    public const string PublisherIdPattern = "^[a-z0-9-]+$";

    /// <summary>
    /// Content ID grammar: an optional publisher prefix followed by a content slug.
    /// </summary>
    public const string ExtendsContentIdPattern = "^([a-z0-9-]+/)?[a-z0-9-]+$";
}
