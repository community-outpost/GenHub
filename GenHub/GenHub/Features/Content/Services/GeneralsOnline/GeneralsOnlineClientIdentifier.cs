using System;
using System.IO;
using System.Linq;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Identifies Generals Online game client executables.
/// </summary>
public class GeneralsOnlineClientIdentifier : IGameClientIdentifier
{
    /// <inheritdoc/>
    public string PublisherId => PublisherTypeConstants.GeneralsOnline;

    /// <inheritdoc/>
    public bool CanIdentify(string executablePath) => IsSupportedEntryPoint(Path.GetFileName(executablePath));

    /// <inheritdoc/>
    public GameClientIdentification? Identify(string executablePath)
    {
        if (!IsSupportedEntryPoint(Path.GetFileName(executablePath)))
        {
            return null;
        }

        return new GameClientIdentification(
            publisherId: PublisherTypeConstants.GeneralsOnline,
            variant: GeneralsOnlineConstants.Variant60HzSuffix,
            displayName: GameClientConstants.GeneralsOnline60HzDisplayName,
            gameType: GameType.ZeroHour,
            localVersion: null); // Don't fetch from web during detection!
    }

    /// <summary>
    /// Determines whether a file name is a supported Generals Online entry point. Since
    /// 060526_QFE1 that is the Easy Anti-Cheat bootstrapper; older packages launch the 60Hz
    /// binary directly. <c>GeneralsOnlineZH.exe</c> ships alongside both but is not wrapped by
    /// Easy Anti-Cheat, so it is workspace content rather than an entry point.
    /// </summary>
    private static bool IsSupportedEntryPoint(string fileName) =>
        GameClientConstants.GeneralsOnlineExecutableNames.Contains(fileName, StringComparer.OrdinalIgnoreCase);
}
