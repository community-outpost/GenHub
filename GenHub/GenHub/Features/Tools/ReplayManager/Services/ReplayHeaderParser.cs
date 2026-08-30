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
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
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

        // 3. Skip 16 bytes (SYSTEMTIME timestamp structure)
        if (offset + 16 > bytesRead)
        {
            return OperationResult<ReplayMetadata>.CreateFailure("Truncated replay header before version string.");
        }

        offset += 16;

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

        // 6. Read numeric version, exeCRC, iniCRC (each 4 bytes uint32 LE)
        if (offset + 12 > bytesRead)
        {
            return OperationResult<ReplayMetadata>.CreateFailure("Truncated replay header before CRC values.");
        }

        var versionNumber = BitConverter.ToUInt32(buffer, offset);
        offset += 4;

        var exeCrc = BitConverter.ToUInt32(buffer, offset);
        offset += 4;

        var iniCrc = BitConverter.ToUInt32(buffer, offset);
        offset += 4;

        // 7. Read Init/Match AsciiString (null-terminated ASCII)
        if (!TryReadNullTerminatedAsciiString(buffer, ref offset, bytesRead, out var initString))
        {
            return OperationResult<ReplayMetadata>.CreateFailure("Unterminated ASCII game options string in replay header.");
        }

        // 8. Extract map name and players from init string if present
        var (mapName, players) = ParseMatchMetadata(initString);

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
                offset += 2;
                value = length == 0 ? null : Encoding.Unicode.GetString(buffer, start, length);
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
                offset += 1;
                value = length == 0 ? null : Encoding.ASCII.GetString(buffer, start, length);
                return true;
            }

            offset += 1;
        }

        value = null;
        return false;
    }

    private static (string? MapName, IReadOnlyList<string>? Players) ParseMatchMetadata(string? initString)
    {
        if (string.IsNullOrWhiteSpace(initString))
        {
            return (null, null);
        }

        string? mapName = null;
        var players = new List<string>();

        var tokens = initString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var token in tokens)
        {
            if (token.StartsWith("M=", StringComparison.OrdinalIgnoreCase))
            {
                mapName = Path.GetFileNameWithoutExtension(token[2..]);
            }
            else if (token.StartsWith("S=", StringComparison.OrdinalIgnoreCase) || token.StartsWith("H=", StringComparison.OrdinalIgnoreCase))
            {
                ExtractSlotPlayers(token[2..], players);
            }
        }

        return (mapName, players.Count > 0 ? players.AsReadOnly() : null);
    }

    private static void ExtractSlotPlayers(string slotData, List<string> players)
    {
        var slots = slotData.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var slot in slots)
        {
            var parts = slot.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            var playerName = CleanPlayerName(parts[0]);
            if (!string.IsNullOrWhiteSpace(playerName) && players.All(p => !string.Equals(p, playerName, StringComparison.OrdinalIgnoreCase)))
            {
                players.Add(playerName);
            }
        }
    }

    private static string CleanPlayerName(string rawName)
    {
        if (rawName.Length > 0 && rawName[0] is 'H' or 'C' or 'X' or 'O' or 'h' or 'c' or 'x' or 'o')
        {
            return rawName[1..];
        }

        return rawName;
    }
}
