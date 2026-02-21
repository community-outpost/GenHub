namespace GenHub.Core.Constants;

/// <summary>
/// Constants for GameReplays integration.
/// </summary>
public static class GameReplaysConstants
{
    /// <summary>
    /// Base URL for GameReplays website.
    /// </summary>
    public const string BaseUrl = "https://www.gamereplays.org";

    /// <summary>
    /// Tournament board topic ID.
    /// </summary>
    public const string TournamentBoardTopicId = "1066376";

    /// <summary>
    /// OAuth 2.0 authorization endpoint.
    /// </summary>
    public const string OAuthAuthorizationEndpoint = "https://www.gamereplays.org/oauth/authorize";

    /// <summary>
    /// OAuth 2.0 token endpoint.
    /// </summary>
    public const string OAuthTokenEndpoint = "https://www.gamereplays.org/oauth/token";

    /// <summary>
    /// OAuth 2.0 resource endpoint.
    /// </summary>
    public const string OAuthResourceEndpoint = "https://www.gamereplays.org/oauth/resource";

    /// <summary>
    /// OAuth 2.0 client ID.
    /// TODO: Replace with actual client ID from GameReplays.
    /// </summary>
    public const string OAuthClientId = "genhub_client";

    /// <summary>
    /// OAuth 2.0 client secret.
    /// TODO: Replace with actual client secret from GameReplays.
    /// </summary>
    public const string OAuthClientSecret = "genhub_secret";

    /// <summary>
    /// OAuth 2.0 redirect URI for callback handling.
    /// Uses custom URI scheme for desktop application.
    /// </summary>
    public const string OAuthRedirectUri = "genhub://oauth/callback";

    /// <summary>
    /// OAuth 2.0 scope for user profile access.
    /// </summary>
    public const string OAuthScope = "user_profile";

    /// <summary>
    /// OAuth state validity period in minutes.
    /// </summary>
    public const int OAuthStateValidityMinutes = 10;

    /// <summary>
    /// Cache duration for tournament data in minutes.
    /// </summary>
    public const int CacheDurationMinutes = 5;

    /// <summary>
    /// Default request timeout in seconds.
    /// </summary>
    public const int DefaultRequestTimeoutSeconds = 30;

    /// <summary>
    /// Maximum retry attempts for failed requests.
    /// </summary>
    public const int MaxRetryAttempts = 3;

    /// <summary>
    /// Delay between retry attempts in milliseconds.
    /// </summary>
    public const int RetryDelayMs = 1000;

    /// <summary>
    /// Forum ID for Zero Hour General Discussion.
    /// </summary>
    public const string ZeroHourGeneralDiscussionForumId = "35";

    /// <summary>
    /// UI colors for tournament statuses.
    /// </summary>
    public static class Colors
    {
        /// <summary>Signups are currently open.</summary>
        public const string SignupsOpen = "#FF9800"; // Orange

        /// <summary>Tournament is upcoming.</summary>
        public const string Upcoming = "#F44336"; // Red

        /// <summary>Tournament is currently active.</summary>
        public const string Active = "#4CAF50"; // Green

        /// <summary>Tournament has finished.</summary>
        public const string Finished = "#9C27B0"; // Purple

        /// <summary>Default status color.</summary>
        public const string Default = "#757575"; // Gray
    }
}
