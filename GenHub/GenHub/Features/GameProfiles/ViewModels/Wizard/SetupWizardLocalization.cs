using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.Common;
using System.Collections.Generic;

namespace GenHub.Features.GameProfiles.ViewModels.Wizard;

/// <summary>
/// Resolves Setup Wizard text by <see cref="GameClientConstants.WizardLocalizationKeys"/> key with its English fallback.
/// </summary>
internal static class SetupWizardLocalization
{
    /// <summary>
    /// Gets and formats the localized wizard text for a key, falling back to the English text in
    /// <see cref="GameClientConstants.WizardFallbackText"/>.
    /// </summary>
    /// <param name="localizationService">The localization service, or null to use English.</param>
    /// <param name="key">A <see cref="GameClientConstants.WizardLocalizationKeys"/> key.</param>
    /// <param name="args">Format arguments.</param>
    /// <returns>The localized and formatted text.</returns>
    public static string GetWizardText(this ILocalizationService? localizationService, string key, params object?[] args) =>
        localizationService.GetLocalizedString(key, GameClientConstants.WizardFallbackText.GetValueOrDefault(key, key), args);
}
