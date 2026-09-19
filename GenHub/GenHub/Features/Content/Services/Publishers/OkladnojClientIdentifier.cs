using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using System;
using System.IO;

namespace GenHub.Features.Content.Services.Publishers;

/// <summary>
/// Identifies the Okladnoj macOS game client executable.
/// </summary>
public class OkladnojClientIdentifier : IGameClientIdentifier
{
    /// <inheritdoc/>
    public string PublisherId => PublisherTypeConstants.Okladnoj;

    /// <inheritdoc/>
    public bool CanIdentify(string executablePath)
    {
        var fileName = Path.GetFileName(executablePath);
        return fileName.Equals(GameClientConstants.OkladnojMacExecutable, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public GameClientIdentification? Identify(string executablePath)
    {
        if (!CanIdentify(executablePath))
        {
            return null;
        }

        return new GameClientIdentification(
            publisherId: PublisherTypeConstants.Okladnoj,
            variant: PublisherTypeConstants.Okladnoj,
            displayName: GameClientConstants.OkladnojZeroHourDisplayName,
            gameType: GameType.ZeroHour,
            localVersion: null); // Don't fetch from web during detection!
    }
}
