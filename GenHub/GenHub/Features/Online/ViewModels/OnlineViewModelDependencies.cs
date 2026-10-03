using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Tools.Checksum;
using System;

namespace GenHub.Features.Online.ViewModels;

/// <summary>
/// Groups optional Online tab dependencies so the ViewModel constructor stays clean.
/// </summary>
/// <param name="LocalizationService">The optional localization service.</param>
/// <param name="UserSettingsService">The optional user settings service.</param>
/// <param name="GameInstallationService">The optional game installation service.</param>
/// <param name="CrcCalculator">The optional game CRC calculator service.</param>
/// <param name="ServiceProvider">The optional service provider.</param>
/// <param name="ManifestPool">The optional content manifest pool.</param>
/// <param name="TimeProvider">The optional clock provider.</param>
public sealed record OnlineViewModelDependencies(
    ILocalizationService? LocalizationService = null,
    IUserSettingsService? UserSettingsService = null,
    IGameInstallationService? GameInstallationService = null,
    IGameCrcCalculatorService? CrcCalculator = null,
    IServiceProvider? ServiceProvider = null,
    IContentManifestPool? ManifestPool = null,
    TimeProvider? TimeProvider = null);
