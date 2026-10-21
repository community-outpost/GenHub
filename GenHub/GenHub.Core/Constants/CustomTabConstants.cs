namespace GenHub.Core.Constants;

/// <summary>
/// Limits and allowlists for publisher-defined custom tabs in the content detail view.
/// </summary>
public static class CustomTabConstants
{
    /// <summary>
    /// The maximum number of custom tabs a single content item or the catalog root may declare.
    /// </summary>
    public const int MaxTabsPerScope = 10;

    /// <summary>
    /// The maximum number of cards a single custom tab may declare.
    /// </summary>
    public const int MaxCardsPerTab = 24;

    /// <summary>
    /// The maximum length of a custom tab identifier.
    /// </summary>
    public const int MaxTabIdLength = 64;

    /// <summary>
    /// The maximum length of a custom tab header.
    /// </summary>
    public const int MaxHeaderLength = 40;

    /// <summary>
    /// The maximum length of a custom tab intro.
    /// </summary>
    public const int MaxIntroLength = 2000;

    /// <summary>
    /// The maximum length of a custom tab Markdown body.
    /// </summary>
    public const int MaxBodyLength = 50000;

    /// <summary>
    /// The URI scheme for images bundled with the application.
    /// </summary>
    public const string AvaloniaResourceScheme = "avares";
}
