using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Tools.Checksum;

/// <summary>
/// Service for calculating SAGE engine executable (exeCRC) and configuration (iniCRC) checksums.
/// </summary>
public interface IGameCrcCalculatorService
{
    /// <summary>
    /// Calculates the 32-bit executable CRC (exeCRC) for a game binary.
    /// </summary>
    /// <param name="executablePath">Path to the game executable or launcher.</param>
    /// <param name="gameRootPath">Optional root directory containing Data/Scripts.</param>
    /// <param name="major">Optional explicit major engine version (auto-detected if null).</param>
    /// <param name="minor">Optional explicit minor engine version (auto-detected if null).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The calculated exeCRC as an uppercase 8-character hex string (e.g. 0x401D89EA), or failure.</returns>
    Task<OperationResult<string>> CalculateExeCrcAsync(
        string executablePath,
        string? gameRootPath = null,
        int? major = null,
        int? minor = null,
        CancellationToken ct = default);

    /// <summary>
    /// Calculates the 32-bit configuration INI CRC (iniCRC) for a game directory with optional sideloads and mods.
    /// </summary>
    /// <param name="gameRootPath">Root directory of the game installation.</param>
    /// <param name="gameType">Target game (ZeroHour or Generals).</param>
    /// <param name="sideloadPaths">Optional list of sideload directories or .big archive paths.</param>
    /// <param name="modPath">Optional mod directory or .big archive path.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The calculated iniCRC as an uppercase 8-character hex string (e.g. 0x76B251A3), or failure.</returns>
    Task<OperationResult<string>> CalculateIniCrcAsync(
        string gameRootPath,
        GameType gameType,
        IReadOnlyList<string>? sideloadPaths = null,
        string? modPath = null,
        CancellationToken ct = default);
}
