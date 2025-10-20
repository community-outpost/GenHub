using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Models.Enums;
using System;
using System.IO;

namespace GenHub.Features.GameSettings;

/// <summary>
/// Windows-specific implementation of game path provider.
/// </summary>
public class WindowsGamePathProvider : IGamePathProvider
{
    private const string GeneralsFolderName = "Command and Conquer Generals Data";
    private const string ZeroHourFolderName = "Command and Conquer Generals Zero Hour Data";

    /// <inheritdoc/>
    public string GetOptionsDirectory(GameType gameType)
    {
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var folderName = gameType == GameType.ZeroHour ? ZeroHourFolderName : GeneralsFolderName;
        return Path.Combine(documentsPath, folderName);
    }
}
