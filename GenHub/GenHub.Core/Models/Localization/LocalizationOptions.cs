namespace GenHub.Core.Models.Localization;

/// <summary>
/// Configuration options for the localization system.
/// </summary>
public class LocalizationOptions
{
    /// <summary>
    /// Gets or sets the default culture code (e.g., "en", "en-US").
    /// </summary>
    /// <remarks>
    /// This is the culture that will be used when the application starts
    /// if no user preference is set.
    /// </remarks>
    public string DefaultCulture { get; set; } = "en";

    /// <summary>
    /// Gets or sets the fallback culture code used when a translation is missing.
    /// </summary>
    /// <remarks>
    /// When a translation key is not found in the current culture,
    /// the system will attempt to use this culture's translation.
    /// </remarks>
    public string FallbackCulture { get; set; } = "en";

    /// <summary>
    /// Gets or sets a value indicating whether to log missing translations.
    /// </summary>
    /// <remarks>
    /// When enabled, missing translation keys will be logged as warnings.
    /// This is useful during development to identify untranslated content.
    /// Default is true to help developers identify missing translations.
    /// </remarks>
    public bool LogMissingTranslations { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to throw an exception when a translation is missing.
    /// </summary>
    /// <remarks>
    /// When enabled, requesting a missing translation key will throw an exception.
    /// This is typically disabled in production to allow graceful degradation.
    /// Default is false to prevent application crashes.
    /// </remarks>
    public bool ThrowOnMissingTranslation { get; set; } = false;
}