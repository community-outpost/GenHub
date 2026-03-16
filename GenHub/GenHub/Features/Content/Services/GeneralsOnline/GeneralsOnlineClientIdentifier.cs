using System;
using System.IO;
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
    public bool CanIdentify(string executablePath)
    {
        var fileName = Path.GetFileName(executablePath);
        return fileName.Equals(GameClientConstants.GeneralsOnline60HzExecutable, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public GameClientIdentification? Identify(string executablePath)
    {
        var fileName = Path.GetFileName(executablePath);

        if (!fileName.Equals(GameClientConstants.GeneralsOnline60HzExecutable, StringComparison.OrdinalIgnoreCase))
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
}
