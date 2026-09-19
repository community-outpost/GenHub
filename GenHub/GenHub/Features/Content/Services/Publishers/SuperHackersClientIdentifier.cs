using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using System;
using System.IO;

namespace GenHub.Features.Content.Services.Publishers;

/// <summary>
/// Identifies The SuperHackers game client executables.
/// </summary>
public class SuperHackersClientIdentifier : IGameClientIdentifier
{
    /// <inheritdoc/>
    public string PublisherId => PublisherTypeConstants.TheSuperHackers;

    /// <inheritdoc/>
    public bool CanIdentify(string executablePath)
    {
        var fileName = Path.GetFileName(executablePath);
        return fileName.Equals(GameClientConstants.SuperHackersGeneralsExecutable, StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals(GameClientConstants.SuperHackersZeroHourExecutable, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public GameClientIdentification? Identify(string executablePath)
    {
        var fileName = Path.GetFileName(executablePath);
        var isGenerals = fileName.Equals(GameClientConstants.SuperHackersGeneralsExecutable, StringComparison.OrdinalIgnoreCase);
        var gameType = isGenerals ? GameType.Generals : GameType.ZeroHour;
        var variant = isGenerals ? SuperHackersConstants.GeneralsSuffix : SuperHackersConstants.ZeroHourSuffix;
        var displayName = isGenerals
            ? $"{SuperHackersConstants.PublisherName} - {SuperHackersConstants.GeneralsDisplayName}"
            : $"{SuperHackersConstants.PublisherName} - {SuperHackersConstants.ZeroHourDisplayName}";

        return new GameClientIdentification(
            publisherId: PublisherTypeConstants.TheSuperHackers,
            variant: variant,
            displayName: displayName,
            gameType: gameType,
            localVersion: null); // Don't fetch from web during detection!
    }
}
