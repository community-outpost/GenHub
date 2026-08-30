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
            return OperationResult<ReplayMetadata>.CreateFailure($"Replay file exceeds maximum allowed size of {ReplayManagerConstants.MaxReplaySizeBytes} bytes.");
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

        try
        {
            // Accumulate stream data to ensure full header buffer is read
            var buffer = new byte[ReplayManagerConstants.ReplayHeaderBufferSize];
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

            if (bytesRead < ReplayManagerConstants.MinReplayHeaderSizeBytes)
            {
                return OperationResult<ReplayMetadata>.CreateFailure("Replay file is too small to contain a valid header.");
            }

            // 1. Verify Magic Header ("GENREP")
            for (var i = 0; i < ExpectedMagic.Length; i++)
            {
                if (buffer[i] != ExpectedMagic[i])
                {
                    return OperationResult<ReplayMetadata>.CreateFailure("Invalid replay file magic header (expected GENREP).");
                }
            }

            var offset = ReplayManagerConstants.ReplayHeaderInitialOffsetBytes;

            // 3. Read VersionString (null-terminated UTF-16LE)
            var versionString = ReadNullTerminatedUtf16String(buffer, ref offset, bytesRead);

            // 4. Skip 16 bytes (timestamp structure)
            if (offset + 16 > bytesRead)
            {
                return OperationResult<ReplayMetadata>.CreateFailure("Truncated replay header before build time string.");
            }

            offset += 16;

            // 5. Read VersionTimeString / BuildTimeString (null-terminated UTF-16LE)
            var buildTimeString = ReadNullTerminatedUtf16String(buffer, ref offset, bytesRead);

            // 6. Read Title/Description (null-terminated UTF-16LE)
            var titleString = ReadNullTerminatedUtf16String(buffer, ref offset, bytesRead);

            // 7. Read numeric version, exeCRC, iniCRC (each 4 bytes uint32 LE)
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

            // 8. Read Init/Match AsciiString (null-terminated ASCII)
            var initString = ReadNullTerminatedAsciiString(buffer, ref offset, bytesRead);

            // 9. Extract map name and players from init string if present
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unexpected error parsing replay header");
            return OperationResult<ReplayMetadata>.CreateFailure($"Failed to parse replay header: {ex.Message}");
        }
    }

    private static string? ReadNullTerminatedUtf16String(byte[] buffer, ref int offset, int maxBytes)
    {
        var start = offset;
        while (offset + 1 < maxBytes)
        {
            if (buffer[offset] == 0 && buffer[offset + 1] == 0)
            {
                var length = offset - start;
                offset += 2;
                return length == 0 ? null : Encoding.Unicode.GetString(buffer, start, length);
            }

            offset += 2;
        }

        return null;
    }

    private static string? ReadNullTerminatedAsciiString(byte[] buffer, ref int offset, int maxBytes)
    {
        var start = offset;
        while (offset < maxBytes)
        {
            if (buffer[offset] == 0)
            {
                var length = offset - start;
                offset += 1;
                return length == 0 ? null : Encoding.ASCII.GetString(buffer, start, length);
            }

            offset += 1;
        }

        return null;
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
                var rawMap = token[2..];
                mapName = Path.GetFileNameWithoutExtension(rawMap);
            }
            else if (token.StartsWith("S=", StringComparison.OrdinalIgnoreCase) || token.StartsWith("H=", StringComparison.OrdinalIgnoreCase))
            {
                var slotData = token[2..];
                var parts = slotData.Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    var rawName = parts[0];
                    var playerName = rawName.Length > 0 && (rawName[0] is 'H' or 'C' or 'X' or 'O' or 'h' or 'c' or 'x' or 'o')
                        ? rawName[1..]
                        : rawName;
                    if (!string.IsNullOrWhiteSpace(playerName) && players.All(p => !string.Equals(p, playerName, StringComparison.OrdinalIgnoreCase)))
                    {
                        players.Add(playerName);
                    }
                }
            }
        }

        return (mapName, players.Count > 0 ? players.AsReadOnly() : null);
    }
}
