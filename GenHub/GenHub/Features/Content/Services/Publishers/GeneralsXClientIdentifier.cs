using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using System;
using System.IO;

namespace GenHub.Features.Content.Services.Publishers;

/// <summary>
/// Identifies GeneralsX game client executables.
/// </summary>
public class GeneralsXClientIdentifier : IGameClientIdentifier
{
    /// <inheritdoc/>
    public string PublisherId => PublisherTypeConstants.GeneralsX;

    /// <inheritdoc/>
    public bool CanIdentify(string executablePath)
    {
        var fileName = Path.GetFileName(executablePath);
        return fileName.Equals(GameClientConstants.GeneralsXWindowsExecutable, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public GameClientIdentification? Identify(string executablePath)
    {
        if (!CanIdentify(executablePath))
        {
            return null;
        }

        return new GameClientIdentification(
            publisherId: PublisherTypeConstants.GeneralsX,
            variant: PublisherTypeConstants.GeneralsX,
            displayName: GameClientConstants.GeneralsXZeroHourDisplayName,
            gameType: GameType.ZeroHour,
            localVersion: null); // Don't fetch from web during detection!
    }
}
