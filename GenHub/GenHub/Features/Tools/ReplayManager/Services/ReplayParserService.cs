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
    // Faction mappings for C&C Generals/Zero Hour
    private static readonly Dictionary<string, string> FactionMap = new()
    {
        { "FactionAmerica", "USA" },
        { "FactionChina", "China" },
        { "FactionGLA", "GLA" },
        { "AmericaAirForceGeneral", "USA Air Force" },
        { "AmericaLaserGeneral", "USA Laser" },
        { "AmericaSuperweaponGeneral", "USA Superweapon" },
        { "ChinaInfantryGeneral", "China Infantry" },
        { "ChinaNukeGeneral", "China Nuke" },
        { "ChinaTankGeneral", "China Tank" },
        { "GLADemolitionGeneral", "GLA Demolition" },
        { "GLAStealthGeneral", "GLA Stealth" },
        { "GLAToxinGeneral", "GLA Toxin" },
    };

    private static readonly Dictionary<string, string> ColorMap = new()
    {
        { "0", "Orange" },
        { "1", "Pink" },
        { "2", "Blue" },
        { "3", "Green" },
        { "4", "Red" },
        { "5", "Yellow" },
        { "6", "Purple" },
        { "7", "Teal" },
    };





    /// <summary>
    /// Parses a replay file and extracts metadata.
    /// </summary>
    /// <param name="filePath">The path to the replay file.</param>
    /// <param name="gameType">The game type.</param>
    /// <returns>The extracted metadata.</returns>
    public Task<ReplayMetadata> ParseReplayAsync(string filePath, GameType gameType)
    {
        // Offload synchronous I/O to thread pool to avoid blocking
        return Task.Run(() => ParseReplayCore(filePath, gameType));
    }

    private ReplayMetadata ParseReplayCore(string filePath, GameType gameType)
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

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);

            var magic = reader.ReadBytes(6);
            var magicString = Encoding.ASCII.GetString(magic);
            if (magicString != ReplayManagerConstants.ReplayMagicBytes)
            {
                logger.LogWarning("Invalid replay file format: {FilePath} (magic: {Magic})", filePath, magicString);
                return CreateEmptyMetadata(filePath, fileInfo.Length, gameType);
            }

            // Read timestamps per GENREP spec (only 2 UInt32 values)
            var beginTimestamp = reader.ReadUInt32();
            var endTimestamp = reader.ReadUInt32();

            // Skip unknown1[12] bytes (Generals/ZH specific)
            reader.ReadBytes(12);

            // Read null-terminated UTF-16 replay filename (e.g. "Last Replay")
            ReadNullTerminatedString(reader, Encoding.Unicode);

            // Skip date_time[8] (8 x uint16 = 16 bytes: year, month, dayOfWeek, day, hour, minute, second, millisecond)
            reader.ReadBytes(16);

            // Read version and build date strings
            var gameVersion = ReadNullTerminatedString(reader, Encoding.Unicode);
            var buildDate = ReadNullTerminatedString(reader, Encoding.Unicode);

            // Skip version_minor (2 bytes) + version_major (2 bytes) + magic_hash[8]
            reader.ReadBytes(12);

            // Read match data — null-terminated ASCII metadata string (key=value pairs separated by ;)
            var matchData = ReadNullTerminatedString(reader, Encoding.ASCII);

            var parsedData = ParseMatchData(matchData);

            var gameDate = beginTimestamp > 0
                ? DateTimeOffset.FromUnixTimeSeconds(beginTimestamp).LocalDateTime
                : fileInfo.LastWriteTime;

            var duration = endTimestamp > beginTimestamp
                ? TimeSpan.FromSeconds(endTimestamp - beginTimestamp)
                : (TimeSpan?)null;

            logger.LogInformation(
                "Successfully parsed replay: {FilePath} (Map: {Map}, Players: {PlayerCount}, Duration: {Duration})",
                filePath,
                parsedData.MapName ?? "Unknown",
                parsedData.Players?.Count ?? 0,
                duration);

            return new ReplayMetadata
            {
                MapName = parsedData.MapName,
                Players = parsedData.Players,
                Duration = duration,
                GameDate = gameDate,
                GameType = gameType,
                FileSizeBytes = fileInfo.Length,
                IsParsed = true,
                OriginalFilePath = filePath,
                GameVersion = string.IsNullOrWhiteSpace(gameVersion) ? null : gameVersion,
                BuildDate = string.IsNullOrWhiteSpace(buildDate) ? null : buildDate,
                GameMode = parsedData.GameMode,
                StartingCredits = parsedData.StartingCredits,
                FogOfWar = parsedData.FogOfWar,
                GameSpeed = parsedData.GameSpeed
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
        // Use CodePage comparison for robust encoding detection
        var charSize = encoding.CodePage == Encoding.Unicode.CodePage ? 2 : 1;

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

    private static ParsedMatchData ParseMatchData(string matchData)
    {
        var result = new ParsedMatchData();

        if (string.IsNullOrWhiteSpace(matchData))
        {
            return result;
        }

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
                    result.MapName = value;
                    break;

                case "MC":
                    result.StartingCredits = int.TryParse(value, out var credits) ? credits : null;
                    break;

                case "MS":
                    result.GameSpeed = value switch
                    {
                        "0" => "Slow",
                        "1" => "Normal",
                        "2" => "Fast",
                        _ => value
                    };
                    break;

                case "SD":
                    result.GameMode = value switch
                    {
                        "0" => "Skirmish",
                        "1" => "Online",
                        _ => value
                    };
                    break;

                case "FOG":
                    result.FogOfWar = value == "1";
                    break;

                case "S":
                    // S field format: slot records separated by colons
                    // Each slot: playerSpec,faction,team,color,...
                    // playerSpec: H<name> for human, C[E|M|H|B] for AI, X for empty
                    result.Players = ParsePlayerSlots(value);
                    break;
            }
        }

        return result;
    }

    private static List<PlayerInfo>? ParsePlayerSlots(string slotsData)
    {
        var players = new List<PlayerInfo>();

        // Split by colon to get individual slot records
        var slots = slotsData.Split(':', StringSplitOptions.RemoveEmptyEntries);

        for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
        {
            var slot = slots[slotIndex].Trim();
            if (string.IsNullOrEmpty(slot) || slot == "X")
            {
                continue; // Empty slot
            }

            // Split slot by comma to get fields - preserve positions for sparse records
            var fields = slot.Split(',');
            if (fields.Length == 0)
            {
                continue;
            }

            var playerSpec = fields[0].Trim();
            // Handle empty fields explicitly to preserve positional semantics
            string? faction = fields.Length > 1 && !string.IsNullOrWhiteSpace(fields[1]) ? fields[1].Trim() : null;
            int? team = fields.Length > 2 && int.TryParse(fields[2].Trim(), out var t) ? t : null;
            string? colorCode = fields.Length > 3 && !string.IsNullOrWhiteSpace(fields[3]) ? fields[3].Trim() : null;

            PlayerInfo? playerInfo = null;

            if (playerSpec.StartsWith('H') && playerSpec.Length > 1)
            {
                // Human player: name follows 'H' prefix
                var playerName = playerSpec[1..];
                playerInfo = new PlayerInfo
                {
                    Name = playerName,
                    Type = PlayerType.Human,
                    Faction = faction != null && FactionMap.TryGetValue(faction, out var factionName) ? factionName : faction,
                    Team = team,
                    Color = colorCode != null && ColorMap.TryGetValue(colorCode, out var color) ? color : colorCode,
                    StartPosition = slotIndex + 1
                };
            }
            else if (playerSpec.StartsWith('C') && playerSpec.Length > 1)
            {
                // Computer player: CE=Easy, CM=Medium, CH=Hard, CB=Brutal
                var difficulty = playerSpec[1] switch
                {
                    'E' => "Easy",
                    'M' => "Medium",
                    'H' => "Hard",
                    'B' => "Brutal",
                    _ => "Unknown"
                };

                playerInfo = new PlayerInfo
                {
                    Name = $"AI ({difficulty})",
                    Type = PlayerType.Computer,
                    AiDifficulty = difficulty,
                    Faction = faction != null && FactionMap.TryGetValue(faction, out var factionName) ? factionName : faction,
                    Team = team,
                    Color = colorCode != null && ColorMap.TryGetValue(colorCode, out var color) ? color : colorCode,
                    StartPosition = slotIndex + 1
                };
            }
            else if (playerSpec.StartsWith('O'))
            {
                // Observer
                var observerName = playerSpec.Length > 1 ? playerSpec[1..] : "Observer";
                playerInfo = new PlayerInfo
                {
                    Name = observerName,
                    Type = PlayerType.Observer,
                    StartPosition = slotIndex + 1
                };
            }

            if (playerInfo != null)
            {
                players.Add(playerInfo);
            }
        }

        return players.Count > 0 ? players : null;
    }

    private sealed class ParsedMatchData
    {
        public string? MapName { get; set; }
        public List<PlayerInfo>? Players { get; set; }
        public string? GameMode { get; set; }
        public int? StartingCredits { get; set; }
        public bool? FogOfWar { get; set; }
        public string? GameSpeed { get; set; }
    }
}
