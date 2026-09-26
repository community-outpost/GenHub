using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Tools.Checksum;
using System;

namespace GenHub.Features.Online.ViewModels;

/// <summary>
/// Groups the optional Online tab services so the ViewModel constructor stays
/// within the analyzer parameter limit. All stay optional: a null record (or
/// a null member) disables localization fallback strings, nickname
/// persistence, installation path resolution, or CRC calculation respectively.
/// </summary>
/// <param name="LocalizationService">The optional localization service.</param>
/// <param name="UserSettingsService">The optional user settings service persisting the nickname.</param>
/// <param name="GameInstallationService">The optional game installation service resolving install roots.</param>
/// <param name="CrcCalculator">The optional game CRC calculator service.</param>
/// <param name="ServiceProvider">The optional service provider for resolving dialogs.</param>
/// <param name="ManifestPool">The optional content manifest pool.</param>
public sealed record OnlineViewModelDependencies(
    ILocalizationService? LocalizationService = null,
    IUserSettingsService? UserSettingsService = null,
    IGameInstallationService? GameInstallationService = null,
    IGameCrcCalculatorService? CrcCalculator = null,
    IServiceProvider? ServiceProvider = null,
    IContentManifestPool? ManifestPool = null);
