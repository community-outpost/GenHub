using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using System;
using System.IO;
using System.Linq;

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
        var fileName = Path.GetFileName(executablePath);
        if (!IsSupportedEntryPoint(fileName))
        {
            return null;
        }

        var isTestEnv = fileName.Equals(GameClientConstants.GeneralsOnlineTestEnvironmentExecutable, StringComparison.OrdinalIgnoreCase) ||
                        fileName.Equals(GameClientConstants.GeneralsOnlineDefaultExecutable, StringComparison.OrdinalIgnoreCase);

        return new GameClientIdentification(
            publisherId: PublisherTypeConstants.GeneralsOnline,
            variant: isTestEnv ? GeneralsOnlineConstants.VariantTestEnvironmentSuffix : GeneralsOnlineConstants.Variant60HzSuffix,
            displayName: isTestEnv ? GameClientConstants.GeneralsOnlineTestEnvironmentDisplayName : GameClientConstants.GeneralsOnline60HzDisplayName,
            gameType: GameType.ZeroHour,
            localVersion: null); // Don't fetch from web during detection!
    }

    /// <summary>
    /// Determines whether a file name is a supported Generals Online entry point.
    /// Since 060526_QFE1 the Easy Anti-Cheat bootstrapper or 60Hz binary serves the 60Hz client,
    /// while GeneralsOnlineZH_TestEnvironment.exe (or GeneralsOnlineZH.exe) serves the test environment client without Easy Anti-Cheat.
    /// </summary>
    private static bool IsSupportedEntryPoint(string fileName) =>
        GameClientConstants.GeneralsOnlineExecutableNames.Contains(fileName, StringComparer.OrdinalIgnoreCase);
}
