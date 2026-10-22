using GenHub.Core.Constants;

namespace GenHub.Tools;

/// <summary>
/// Options for configuring the CSV generator execution.
/// </summary>
/// <param name="InstallDir">The game installation directory to scan.</param>
/// <param name="OutputPath">The output CSV file path.</param>
/// <param name="GameType">The target game type ("Generals" or "ZeroHour").</param>
/// <param name="Version">The target game version (e.g. "1.08" or "1.04").</param>
/// <param name="Language">The canonical language code for localized files (default: "EN").</param>
/// <param name="IndexFilePath">The optional path to index.json for updating metadata.</param>
/// <param name="DownloadUrl">The optional download URL override or template.</param>
/// <param name="UpdateIndex">Whether to update the index.json metadata upon successful generation.</param>
public sealed record CsvGeneratorOptions(
    string InstallDir,
    string OutputPath,
    string GameType,
    string Version,
    string Language = CsvConstants.LanguageEn,
    string? IndexFilePath = null,
    string? DownloadUrl = null,
    bool UpdateIndex = false);
