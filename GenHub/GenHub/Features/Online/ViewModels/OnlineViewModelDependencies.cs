using GenHub.Core.Interfaces.Common;

namespace GenHub.Features.Online.ViewModels;

/// <summary>
/// Groups the optional Online tab services so the ViewModel constructor stays
/// within the analyzer parameter limit. Both stay optional: a null record (or
/// a null member) disables localization fallback strings and nickname
/// persistence respectively.
/// </summary>
/// <param name="LocalizationService">The optional localization service.</param>
/// <param name="UserSettingsService">The optional user settings service persisting the nickname.</param>
public sealed record OnlineViewModelDependencies(
    ILocalizationService? LocalizationService = null,
    IUserSettingsService? UserSettingsService = null);
