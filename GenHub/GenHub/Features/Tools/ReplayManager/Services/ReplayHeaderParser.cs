using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.ReplayManager;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ReplayManager.Services;

/// <summary>
/// Binary parser for C&amp;C Generals and Zero Hour .rep replay headers.
/// Extracts game client version, exe/ini CRCs, timestamp, map name, title, and player information.
/// </summary>
public sealed class ReplayHeaderParser(ILogger<ReplayHeaderParser> logger) : IReplayHeaderParser
{
    private static readonly byte[] ExpectedMagic = Encoding.ASCII.GetBytes(ReplayManagerConstants.ReplayHeaderMagic);

    /// <inheritdoc />
    public async Task<OperationResult<ReplayMetadata>> ParseHeaderAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return OperationResult<ReplayMetadata>.CreateFailure("Replay file path cannot be null or empty.");
        }

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            return OperationResult<ReplayMetadata>.CreateFailure($"Replay file not found: {filePath}");
        }

        if (fileInfo.Length > ReplayManagerConstants.MaxReplaySizeBytes)
        {
            return OperationResult<ReplayMetadata>.CreateFailure(
                $"Replay file size ({fileInfo.Length} bytes) exceeds maximum allowed size ({ReplayManagerConstants.MaxReplaySizeBytes} bytes).");
        }

        try
        {
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);

            return await ParseHeaderAsync(stream, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "I/O error while reading replay header from {Path}", filePath);
            return OperationResult<ReplayMetadata>.CreateFailure($"Error reading replay file: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<ReplayMetadata>> ParseHeaderAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (stream.CanSeek && stream.Length > ReplayManagerConstants.MaxReplaySizeBytes)
        {
            return OperationResult<ReplayMetadata>.CreateFailure(
                $"Replay stream size ({stream.Length} bytes) exceeds maximum allowed size ({ReplayManagerConstants.MaxReplaySizeBytes} bytes).");
        }

        try
        {
            var buffer = new byte[ReplayManagerConstants.ReplayHeaderBufferSize];
            var bytesRead = await ReadHeaderBufferAsync(stream, buffer, cancellationToken);

            if (bytesRead < ReplayManagerConstants.MinReplayHeaderSizeBytes)
            {
                return OperationResult<ReplayMetadata>.CreateFailure("Replay file is too small to contain a valid header.");
            }

            if (!IsValidMagic(buffer))
            {
                return OperationResult<ReplayMetadata>.CreateFailure("Invalid replay file magic header (expected GENREP).");
            }

            return ParseHeaderBuffer(buffer, bytesRead);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unexpected error parsing replay header");
            return OperationResult<ReplayMetadata>.CreateFailure($"Failed to parse replay header: {ex.Message}");
        }
    }

    private static async Task<int> ReadHeaderBufferAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var bytesRead = 0;
        while (bytesRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(bytesRead, buffer.Length - bytesRead), cancellationToken);
            if (read == 0)
            {
                break;
            }

            bytesRead += read;
        }

        return bytesRead;
    }

    private static bool IsValidMagic(byte[] buffer)
    {
        for (var i = 0; i < ExpectedMagic.Length; i++)
        {
            if (buffer[i] != ExpectedMagic[i])
            {
                return false;
            }
        }

        return true;
    }

    private static OperationResult<ReplayMetadata> ParseHeaderBuffer(byte[] buffer, int bytesRead)
    {
        var offset = ReplayManagerConstants.ReplayHeaderInitialOffsetBytes;

        // 2. Read Replay Title / Name (null-terminated UTF-16LE, written first by engine)
        if (!TryReadNullTerminatedUtf16String(buffer, ref offset, bytesRead, out var titleString))
        {
            return OperationResult<ReplayMetadata>.CreateFailure("Unterminated UTF-16 replay title string in replay header.");
        }

        // 3. Skip SYSTEMTIME timestamp structure
        if (offset + ReplayManagerConstants.ReplayHeaderSystemTimeSizeBytes > bytesRead)
        {
            return OperationResult<ReplayMetadata>.CreateFailure("Truncated replay header before version string.");
        }

        offset += ReplayManagerConstants.ReplayHeaderSystemTimeSizeBytes;

        // 4. Read VersionString (null-terminated UTF-16LE, e.g. "Version 1.04")
        if (!TryReadNullTerminatedUtf16String(buffer, ref offset, bytesRead, out var versionString))
        {
            return OperationResult<ReplayMetadata>.CreateFailure("Unterminated UTF-16 version string in replay header.");
        }

        // 5. Read VersionTimeString / BuildTimeString (null-terminated UTF-16LE, e.g. "Sep 16 2003")
        if (!TryReadNullTerminatedUtf16String(buffer, ref offset, bytesRead, out var buildTimeString))
        {
            return OperationResult<ReplayMetadata>.CreateFailure("Unterminated UTF-16 build time string in replay header.");
        }

        // 6. Read numeric version, exeCRC, iniCRC (each uint32 LE)
        if (offset + ReplayManagerConstants.ReplayHeaderCrcBlockSizeBytes > bytesRead)
        {
            return OperationResult<ReplayMetadata>.CreateFailure("Truncated replay header before CRC values.");
        }

        var versionNumber = BitConverter.ToUInt32(buffer, offset);
        offset += ReplayManagerConstants.ReplayHeaderUInt32SizeBytes;

        var exeCrc = BitConverter.ToUInt32(buffer, offset);
        offset += ReplayManagerConstants.ReplayHeaderUInt32SizeBytes;

        var iniCrc = BitConverter.ToUInt32(buffer, offset);
        offset += ReplayManagerConstants.ReplayHeaderUInt32SizeBytes;

        // 7. Read Init/Match AsciiString (null-terminated ASCII)
        if (!TryReadNullTerminatedAsciiString(buffer, ref offset, bytesRead, out var initString))
        {
            return OperationResult<ReplayMetadata>.CreateFailure("Unterminated ASCII game options string in replay header.");
        }

        // 8. Extract map name, players, and structured slots from init string if present
        var (mapName, players, slots) = ParseMatchMetadata(initString);

        var metadata = new ReplayMetadata
        {
            VersionString = string.IsNullOrWhiteSpace(versionString) ? null : versionString,
            BuildTimeString = string.IsNullOrWhiteSpace(buildTimeString) ? null : buildTimeString,
            Title = string.IsNullOrWhiteSpace(titleString) ? null : titleString,
            VersionNumber = versionNumber,
            ExeCrc = exeCrc,
            IniCrc = iniCrc,
            MapName = mapName,
            Players = players,
            Slots = slots,
        };

        return OperationResult<ReplayMetadata>.CreateSuccess(metadata);
    }

    private static bool TryReadNullTerminatedUtf16String(byte[] buffer, ref int offset, int maxBytes, out string? value)
    {
        var start = offset;
        while (offset + 1 < maxBytes)
        {
            if (buffer[offset] == 0 && buffer[offset + 1] == 0)
            {
                var length = offset - start;
                value = length > 0 ? Encoding.Unicode.GetString(buffer, start, length) : string.Empty;
                offset += 2;
                return true;
            }

            offset += 2;
        }

        value = null;
        return false;
    }

    private static bool TryReadNullTerminatedAsciiString(byte[] buffer, ref int offset, int maxBytes, out string? value)
    {
        var start = offset;
        while (offset < maxBytes)
        {
            if (buffer[offset] == 0)
            {
                var length = offset - start;
                value = length > 0 ? Encoding.ASCII.GetString(buffer, start, length) : string.Empty;
                offset += 1;
                return true;
            }

            offset += 1;
        }

        value = null;
        return false;
    }

    private static (string? MapName, IReadOnlyList<string>? Players, IReadOnlyList<ReplaySlotInfo>? Slots) ParseMatchMetadata(string? initString)
    {
        if (string.IsNullOrWhiteSpace(initString))
        {
            return (null, null, null);
        }

        string? mapName = null;
        var players = new List<string>();
        var slots = new List<ReplaySlotInfo>();

        var tokens = initString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var token in tokens)
        {
            if (token.StartsWith("M=", StringComparison.OrdinalIgnoreCase))
            {
                mapName = Path.GetFileNameWithoutExtension(token[2..]);
            }
            else if (token.StartsWith("S=", StringComparison.OrdinalIgnoreCase))
            {
                ExtractSlotPlayers(token[2..], players, slots, isSlotDefinition: true);
            }
            else if (token.StartsWith("H=", StringComparison.OrdinalIgnoreCase))
            {
                ExtractSlotPlayers(token[2..], players, slots, isSlotDefinition: false);
            }
        }

        return (
            mapName,
            players.Count > 0 ? players.AsReadOnly() : null,
            slots.Count > 0 ? slots.AsReadOnly() : null);
    }

    private static void ExtractSlotPlayers(
        string slotData,
        List<string> players,
        List<ReplaySlotInfo> structuredSlots,
        bool isSlotDefinition)
    {
        var slots = slotData.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < slots.Length; i++)
        {
            if (TryParseSlot(i, slots[i], isSlotDefinition, out var playerName, out var slotInfo))
            {
                if (playerName != null && !players.Contains(playerName, StringComparer.OrdinalIgnoreCase))
                {
                    players.Add(playerName);
                }

                if (slotInfo != null)
                {
                    structuredSlots.Add(slotInfo);
                }
            }
        }
    }

    private static bool TryParseSlot(
        int slotIndex,
        string slot,
        bool isSlotDefinition,
        out string? playerName,
        out ReplaySlotInfo? slotInfo)
    {
        playerName = null;
        slotInfo = null;

        var parts = slot.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var rawMarkerAndName = parts[0];
        var cleaned = CleanPlayerName(rawMarkerAndName, isSlotDefinition);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        playerName = cleaned;
        if (isSlotDefinition)
        {
            var isHuman = !rawMarkerAndName.StartsWith('C') && !rawMarkerAndName.StartsWith('c');
            var (colorIndex, factionIndex) = ParseSlotIndices(parts, isHuman);

            slotInfo = new ReplaySlotInfo(
                slotIndex,
                playerName,
                isHuman,
                factionIndex,
                colorIndex);
        }

        return true;
    }

    private static (int? Color, int? Faction) ParseSlotIndices(string[] parts, bool isHuman)
    {
        // Wire format per GameInfo.cpp (GameInfoToAsciiString):
        // Human: H<name>,IP,port,flags,color,template,...
        // AI:    C<difficulty>,color,template,startPos,team
        return isHuman
            ? (ParseSlotPart(parts, 4), ParseSlotPart(parts, 5))
            : (ParseSlotPart(parts, 1), ParseSlotPart(parts, 2));
    }

    private static int? ParseSlotPart(string[] parts, int index)
    {
        return parts.Length > index && int.TryParse(parts[index], out var val) ? val : null;
    }

    /// <summary>
    /// Cleans a player name extracted from the replay slot string (S= token) or match header.
    /// In the C&amp;C Generals and Zero Hour network protocol, each player slot entry in the S= token
    /// prepends a single uppercase character slot marker ('H' for Human, 'C' for Computer) directly to the player name.
    /// Standalone slot status indicators ('H', 'C', 'X', 'O') with no name represent empty, open, or closed slots.
    /// </summary>
    /// <param name="rawName">The raw player or slot token from the replay init string.</param>
    /// <param name="isSlotDefinition">Whether the token originated from an S= slot definition with prepended status markers.</param>
    /// <returns>The cleaned player name, or an empty string if the slot represents a non-player status.</returns>
    private static string CleanPlayerName(string rawName, bool isSlotDefinition)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return string.Empty;
        }

        var trimmed = rawName.Trim();
        if (!isSlotDefinition)
        {
            return trimmed;
        }

        if (trimmed.Length == 1)
        {
            // Standalone slot status markers with no player name: 'H' (Human), 'C' (Computer), 'X' (Closed), 'O' (Open)
            return trimmed[0] is 'H' or 'C' or 'h' or 'c' or 'X' or 'O' or 'x' or 'o' ? string.Empty : trimmed;
        }

        // In C&C Generals wire format, slot entries in S= prepend uppercase 'H' (Human) or 'C' (Computer) to the player name.
        if (trimmed[0] is 'H' or 'h')
        {
            return trimmed[1..].Trim();
        }

        if (trimmed[0] is 'C' or 'c')
        {
            var diff = trimmed[1..].Trim();
            return diff switch
            {
                "E" or "e" => "AI (Easy)",
                "M" or "m" => "AI (Medium)",
                "H" or "h" => "AI (Hard)",
                _ => diff,
            };
        }

        return trimmed;
    }
}
