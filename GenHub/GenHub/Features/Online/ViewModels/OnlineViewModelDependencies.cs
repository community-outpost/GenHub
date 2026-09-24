using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;

namespace GenHub.Features.Online.ViewModels;

/// <summary>
/// Groups the optional Online tab services so the ViewModel constructor stays
/// within the analyzer parameter limit. All stay optional: a null record (or
/// a null member) disables localization fallback strings, nickname
/// persistence, or installation path resolution respectively.
/// </summary>
/// <param name="LocalizationService">The optional localization service.</param>
/// <param name="UserSettingsService">The optional user settings service persisting the nickname.</param>
/// <param name="GameInstallationService">The optional game installation service resolving install roots.</param>
public sealed record OnlineViewModelDependencies(
    ILocalizationService? LocalizationService = null,
    IUserSettingsService? UserSettingsService = null,
    IGameInstallationService? GameInstallationService = null);
