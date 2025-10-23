using GenHub.Core.Constants;
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
    /// <inheritdoc/>
    public string GetOptionsDirectory(GameType gameType)
    {
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var folderName = gameType == GameType.ZeroHour
            ? GameSettingsConstants.FolderNames.ZeroHour
            : GameSettingsConstants.FolderNames.Generals;
        return Path.Combine(documentsPath, folderName);
    }
}
