// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Interfaces.Common;
using System;

namespace GenHub.Features.Tools.WorldBuilder.Common;

/// <summary>
/// Gates one-time tutorial hint toasts (QT-03). Hints show through
/// <see cref="GenHub.Core.Interfaces.Notifications.INotificationService"/> only
/// while the user keeps tutorial popups enabled, and each hint key is recorded
/// once dismissed so it never nags twice.
/// </summary>
public static class WbTutorialHints
{
    /// <summary>
    /// Reports whether a hint may show: tutorial popups enabled and the key not
    /// yet dismissed.
    /// </summary>
    /// <param name="settingsService">The user settings service.</param>
    /// <param name="hintKey">The stable hint key.</param>
    /// <returns>True when the hint may show.</returns>
    public static bool ShouldShowHint(IUserSettingsService settingsService, string hintKey)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentException.ThrowIfNullOrWhiteSpace(hintKey);
        var worldBuilder = settingsService.Get().WorldBuilder;
        return worldBuilder.ShowTutorialHints && !worldBuilder.DismissedHints.Contains(hintKey);
    }

    /// <summary>
    /// Records a hint as dismissed. Callers persist via
    /// <see cref="IUserSettingsService.SaveAsync"/> on their own schedule.
    /// </summary>
    /// <param name="settingsService">The user settings service.</param>
    /// <param name="hintKey">The stable hint key.</param>
    public static void DismissHint(IUserSettingsService settingsService, string hintKey)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentException.ThrowIfNullOrWhiteSpace(hintKey);
        settingsService.Update(settings => settings.WorldBuilder.DismissedHints.Add(hintKey));
    }
}
