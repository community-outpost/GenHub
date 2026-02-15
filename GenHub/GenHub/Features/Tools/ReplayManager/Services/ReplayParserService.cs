using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.ReplayManager;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ReplayManager.Services;

/// <summary>
/// Service for parsing replay file headers and extracting metadata.
/// </summary>
/// <param name="logger">The logger instance.</param>
public sealed class ReplayParserService(ILogger<ReplayParserService> logger)
{
    /// <summary>
    /// Parses a replay file and extracts metadata.
    /// </summary>
    /// <param name="filePath">The path to the replay file.</param>
    /// <param name="gameType">The game type.</param>
    /// <returns>The extracted metadata.</returns>
    public async Task<ReplayMetadata> ParseReplayAsync(string filePath, GameType gameType)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
            {
                logger.LogWarning("Replay file not found: {FilePath}", filePath);
                return CreateEmptyMetadata(filePath, 0, gameType);
            }

            if (fileInfo.Length < ReplayManagerConstants.MinimumReplayFileSizeBytes)
            {
                logger.LogWarning("Replay file too small: {FilePath} ({Size} bytes)", filePath, fileInfo.Length);
                return CreateEmptyMetadata(filePath, fileInfo.Length, gameType);
            }

            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);

            var magic = reader.ReadBytes(6);
            var magicString = Encoding.ASCII.GetString(magic);
            if (magicString != ReplayManagerConstants.ReplayMagicBytes)
            {
                logger.LogWarning("Invalid replay file format: {FilePath} (magic: {Magic})", filePath, magicString);
                return CreateEmptyMetadata(filePath, fileInfo.Length, gameType);
            }

            // Read timestamps per GENREP spec
            var beginTimestamp = reader.ReadUInt32();
            var endTimestamp = reader.ReadUInt32();

            // Skip unknown1[12] bytes (Generals/ZH specific)
            reader.ReadBytes(12);

            // Read null-terminated UTF-16 replay filename (e.g. "Last Replay")
            ReadNullTerminatedString(reader, Encoding.Unicode);

            // Skip date_time[8] (8 x uint16 = 16 bytes: year, month, dayOfWeek, day, hour, minute, second, millisecond)
            reader.ReadBytes(16);

            // Read version and build date strings (skip them)
            ReadNullTerminatedString(reader, Encoding.Unicode);
            ReadNullTerminatedString(reader, Encoding.Unicode);

            // Skip version_minor (2 bytes) + version_major (2 bytes) + magic_hash[8]
            reader.ReadBytes(12);

            // Read match data — null-terminated ASCII metadata string (key=value pairs separated by ;)
            var matchData = ReadNullTerminatedString(reader, Encoding.ASCII);

            var (mapName, players) = ParseMatchData(matchData);

            var gameDate = beginTimestamp > 0
                ? DateTimeOffset.FromUnixTimeSeconds(beginTimestamp).LocalDateTime
                : fileInfo.LastWriteTime;

            var duration = endTimestamp > beginTimestamp
                ? TimeSpan.FromSeconds(endTimestamp - beginTimestamp)
                : (TimeSpan?)null;

            logger.LogInformation(
                "Successfully parsed replay: {FilePath} (Map: {Map}, Players: {PlayerCount}, Duration: {Duration})",
                filePath,
                mapName ?? "Unknown",
                players?.Count ?? 0,
                duration);

            return new ReplayMetadata
            {
                MapName = mapName,
                Players = players,
                Duration = duration,
                GameDate = gameDate,
                GameType = gameType,
                FileSizeBytes = fileInfo.Length,
                IsParsed = true,
                OriginalFilePath = filePath,
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing replay file: {FilePath}", filePath);

            long fileSize = 0;
            try
            {
                var errorFileInfo = new FileInfo(filePath);
                if (errorFileInfo.Exists)
                {
                    fileSize = errorFileInfo.Length;
                }
            }
            catch
            {
                // Path may be invalid; use 0 as fallback
            }

            return CreateEmptyMetadata(filePath, fileSize, gameType);
        }
    }

    private static ReplayMetadata CreateEmptyMetadata(string filePath, long fileSize, GameType gameType)
    {
        DateTime gameDate;
        try
        {
            gameDate = File.Exists(filePath) ? File.GetLastWriteTime(filePath) : DateTime.Now;
        }
        catch
        {
            gameDate = DateTime.Now;
        }

        return new ReplayMetadata
        {
            GameDate = gameDate,
            GameType = gameType,
            FileSizeBytes = fileSize,
            IsParsed = false,
            OriginalFilePath = filePath,
        };
    }

    private static string ReadNullTerminatedString(BinaryReader reader, Encoding encoding)
    {
        var bytes = new List<byte>(ReplayManagerConstants.MaxStringReadBytes);
        var charSize = encoding == Encoding.Unicode ? 2 : 1;

        while (bytes.Count + charSize <= ReplayManagerConstants.MaxStringReadBytes)
        {
            var charBytes = reader.ReadBytes(charSize);
            if (charBytes.Length < charSize || IsNullTerminator(charBytes, charSize))
            {
                break;
            }

            bytes.AddRange(charBytes);
        }

        return bytes.Count > 0 ? encoding.GetString(bytes.ToArray()) : string.Empty;
    }

    private static bool IsNullTerminator(byte[] bytes, int charSize)
    {
        if (charSize == 2)
        {
            return bytes.Length >= 2 && bytes[0] == 0 && bytes[1] == 0;
        }

        return bytes.Length >= 1 && bytes[0] == 0;
    }

    private static (string? MapName, IReadOnlyList<string>? Players) ParseMatchData(string matchData)
    {
        if (string.IsNullOrWhiteSpace(matchData))
        {
            return (null, null);
        }

        string? mapName = null;
        var players = new List<string>();

        var entries = matchData.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var entry in entries)
        {
            var separatorIndex = entry.IndexOf('=');
            if (separatorIndex < 0)
            {
                continue;
            }

            var key = entry[..separatorIndex].Trim();
            var value = entry[(separatorIndex + 1)..].Trim();

            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            switch (key)
            {
                case "M":
                    mapName = value;
                    break;

                case "S":
                    // S field is colon-separated slots, each slot is comma-separated fields
                    // Field[0] = player spec: H<name> for human, C[E|M|H|B] for computer, X for empty
                    var slots = value.Split(':', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var slot in slots)
                    {
                        var trimmedSlot = slot.Trim();
                        if (string.IsNullOrEmpty(trimmedSlot) || trimmedSlot == "X")
                        {
                            continue;
                        }

                        var fields = trimmedSlot.Split(',', StringSplitOptions.RemoveEmptyEntries);
                        if (fields.Length == 0)
                        {
                            continue;
                        }

                        var playerSpec = fields[0].Trim();
                        if (playerSpec.StartsWith('H') && playerSpec.Length > 1)
                        {
                            // Human player: name follows 'H' prefix
                            players.Add(playerSpec[1..]);
                        }
                        else if (playerSpec.StartsWith('C') && playerSpec.Length > 1)
                        {
                            // Computer player: CE=Easy, CM=Medium, CH=Hard, CB=Brutal
                            players.Add("CPU");
                        }
                    }

                    break;
            }
        }

        return (mapName, players.Count > 0 ? players : null);
    }
}
