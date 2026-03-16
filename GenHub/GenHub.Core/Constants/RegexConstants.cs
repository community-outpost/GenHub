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
}