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
    /// The list of supported languages for the FAQ.
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedFaqLanguages = new[]
    {
        "en", "de", "ph", "ar",
    };
}
