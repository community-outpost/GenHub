using GenHub.Core.Constants;
using System;
using System.Globalization;
using System.Resources;

namespace GenHub.Common.Validation;

/// <summary>
/// Resolves localized validation messages from the shared string resources.
/// Follows the application language via <see cref="CultureInfo.CurrentUICulture"/>.
/// </summary>
internal static class ValidationResourceResolver
{
    private static readonly ResourceManager ResourceManager = new(
        LocalizationConstants.StringResourceBaseName,
        typeof(ValidationResourceResolver).Assembly);

    /// <summary>
    /// Gets the localized template for a resource key, or null when unavailable.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <returns>The localized template, or null when lookup fails.</returns>
    public static string? GetTemplate(string resourceKey)
    {
        try
        {
            return ResourceManager.GetString(resourceKey, CultureInfo.CurrentUICulture);
        }
        catch (Exception ex) when (ex is MissingManifestResourceException or MissingSatelliteAssemblyException)
        {
            return null;
        }
    }

    /// <summary>
    /// Formats a validation message from a resource key with an English fallback.
    /// </summary>
    /// <param name="resourceKey">The resource key.</param>
    /// <param name="fallbackMessage">The English fallback message.</param>
    /// <param name="args">Optional format arguments (display name first).</param>
    /// <returns>The formatted validation message.</returns>
    public static string FormatMessage(string resourceKey, string fallbackMessage, params object?[] args)
    {
        var template = GetTemplate(resourceKey);
        if (string.IsNullOrEmpty(template) || string.Equals(template, resourceKey, StringComparison.Ordinal))
        {
            template = fallbackMessage;
        }

        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }
}
