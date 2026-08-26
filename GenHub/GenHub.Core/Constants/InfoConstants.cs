using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants for the Info and FAQ features.
/// </summary>
[SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Centralized URI constants / mock demo paths")]
public static class InfoConstants
{
    /// <summary>
    /// The base URL for the FAQ page.
    /// </summary>
    public const string FaqBaseUrl = "https://legi.cc/bugs-solutions-and-faq/";

    /// <summary>
    /// The default language for FAQs.
    /// </summary>
    public const string FaqDefaultLanguage = "en";

    /// <summary>
    /// Section ID for the quickstart guide.
    /// </summary>
    public const string QuickstartSectionId = "quickstart";

    /// <summary>
    /// Module name for GenHub Guide.
    /// </summary>
    public const string ModuleGuide = "GenHub Guide";

    /// <summary>
    /// Module name for Zero Hour.
    /// </summary>
    public const string ModuleZeroHour = "Zero Hour";

    /// <summary>
    /// Module name for GeneralsOnline.
    /// </summary>
    public const string ModuleGeneralsOnline = "GeneralsOnline";

    /// <summary>
    /// Section ID for FAQ.
    /// </summary>
    public const string SectionFaq = "faq";

    /// <summary>
    /// Section ID for GeneralsOnline changelog.
    /// </summary>
    public const string SectionGoChangelog = "go-changelog";

    /// <summary>
    /// The list of supported languages for the FAQ.
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedFaqLanguages =
    [
        "en", "de", "ph", "ar",
    ];
}
