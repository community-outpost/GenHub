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
        return MatchesExecutableName(executablePath, GameClientConstants.SuperHackersGeneralsExecutable) ||
               MatchesExecutableName(executablePath, GameClientConstants.SuperHackersZeroHourExecutable);
    }

    /// <inheritdoc/>
    public GameClientIdentification? Identify(string executablePath)
    {
        if (!CanIdentify(executablePath))
        {
            return null;
        }

        var isGenerals = MatchesExecutableName(executablePath, GameClientConstants.SuperHackersGeneralsExecutable);
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

    /// <summary>
    /// Determines whether a file carries a known executable name, with or without its
    /// <c>.exe</c> extension.
    /// </summary>
    /// <param name="filePath">The candidate file path.</param>
    /// <param name="windowsExecutableName">The Windows name of the executable, ending in <c>.exe</c>.</param>
    /// <returns>True when the file is that executable in Windows or native form.</returns>
    internal static bool MatchesExecutableName(string filePath, string windowsExecutableName)
    {
        var fileName = Path.GetFileName(filePath);

        if (string.Equals(fileName, windowsExecutableName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // A native build of the same client drops the extension: generalszh.exe on
        // Windows is GeneralsZH as a Mach-O or ELF binary.
        return !Path.HasExtension(fileName)
            && string.Equals(
                fileName,
                Path.GetFileNameWithoutExtension(windowsExecutableName),
                StringComparison.OrdinalIgnoreCase);
    }
}
