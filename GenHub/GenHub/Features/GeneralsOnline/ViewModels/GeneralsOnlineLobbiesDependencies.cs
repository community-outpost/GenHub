using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GeneralsOnline;
using GenHub.Core.Interfaces.Online;
using Microsoft.Extensions.Logging;
using System;

namespace GenHub.Features.GeneralsOnline.ViewModels;

/// <summary>
/// Groups the optional Generals Online lobby services so the ViewModel constructor stays
/// within the analyzer parameter limit. A null member degrades gracefully: no live hints,
/// no launching, no compatibility matching, or no localized fallbacks respectively.
/// </summary>
/// <param name="WsListener">The optional WebSocket hint listener.</param>
/// <param name="LaunchService">The optional online launch service.</param>
/// <param name="ProfileManager">The optional game profile manager.</param>
/// <param name="LocalizationService">The optional localization service.</param>
/// <param name="ServiceProvider">The optional service provider for resolving dialogs.</param>
/// <param name="Logger">The optional logger for diagnostic output and browser launches.</param>
public sealed record GeneralsOnlineLobbiesDependencies(
    IGeneralsOnlineWebSocketListener? WsListener = null,
    IOnlineLaunchService? LaunchService = null,
    IGameProfileManager? ProfileManager = null,
    ILocalizationService? LocalizationService = null,
    IServiceProvider? ServiceProvider = null,
    ILogger? Logger = null);
