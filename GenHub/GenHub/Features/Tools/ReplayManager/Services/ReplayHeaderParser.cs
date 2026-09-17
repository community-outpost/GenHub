using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.ReplayManager;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ReplayManager.Services;

/// <summary>
/// Binary parser for C&amp;C Generals and Zero Hour .rep replay headers.
/// Extracts game client version, exe/ini CRCs, timestamp, map name, title, and player information.
/// </summary>
public sealed class ReplayHeaderParser(ILogger<ReplayHeaderParser> logger) : IReplayHeaderParser
{
    private static readonly byte[] ExpectedMagic = Encoding.ASCII.GetBytes(ReplayManagerConstants.ReplayHeaderMagic);

    private readonly record struct ReplayTimingContext(
        uint StartTime,
        uint EndTime,
        uint HeaderFrameCount,
        string? VersionString,
        string? BuildTimeString,
        string? TitleString);

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

        if (!stream.CanRead)
        {
            return OperationResult<ReplayMetadata>.CreateFailure("Stream does not support reading.");
        }

        if (stream.CanSeek && stream.Length > ReplayManagerConstants.MaxReplaySizeBytes)
        {
            return OperationResult<ReplayMetadata>.CreateFailure(
                $"Replay stream size ({stream.Length} bytes) exceeds maximum allowed size ({ReplayManagerConstants.MaxReplaySizeBytes} bytes).");
        }

        try
        {
            // Read up to MaxHeaderReadBytes to cover title, strings, CRCs, and match setup
            var buffer = new byte[ReplayManagerConstants.MaxHeaderReadBytes];
            var bytesRead = await ReadHeaderBufferAsync(stream, buffer, cancellationToken);

            if (bytesRead < ReplayManagerConstants.MinHeaderReadBytes)
            {
                return OperationResult<ReplayMetadata>.CreateFailure("Replay file is too small to contain a valid header.");
            }

            // Verify "GENREP" magic bytes
            if (!IsValidMagic(buffer))
            {
                return OperationResult<ReplayMetadata>.CreateFailure("Invalid replay file magic header (expected GENREP).");
            }

            return ParseHeaderBuffer(buffer, bytesRead);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "I/O error while reading replay header from stream");
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
        var startTime = bytesRead >= ReplayManagerConstants.StartTimeOffsetBytes + sizeof(uint)
            ? BitConverter.ToUInt32(buffer, ReplayManagerConstants.StartTimeOffsetBytes)
            : 0u;
        var endTime = bytesRead >= ReplayManagerConstants.EndTimeOffsetBytes + sizeof(uint)
            ? BitConverter.ToUInt32(buffer, ReplayManagerConstants.EndTimeOffsetBytes)
            : 0u;
        var headerFrameCount = bytesRead >= ReplayManagerConstants.HeaderFrameCountOffsetBytes + sizeof(uint)
            ? BitConverter.ToUInt32(buffer, ReplayManagerConstants.HeaderFrameCountOffsetBytes)
            : 0u;

        var offset = ReplayManagerConstants.ReplayHeaderInitialOffsetBytes;

        // 2. Read Replay Title / Name (null-terminated UTF-16LE, written first by engine)
        if (!TryReadNullTerminatedUtf16String(buffer, ref offset, bytesRead, out var titleString))
        {
            return OperationResult<ReplayMetadata>.CreateFailure("Unterminated UTF-16 replay title string in replay header.");
        }

        // 3. Read SYSTEMTIME timestamp structure
        if (offset + ReplayManagerConstants.ReplayHeaderSystemTimeSizeBytes > bytesRead)
        {
            return OperationResult<ReplayMetadata>.CreateFailure("Truncated replay header before version string.");
        }

        var gameDate = TryParseSystemTime(buffer, offset);

        offset += ReplayManagerConstants.ReplayHeaderSystemTimeSizeBytes;

        // 4. Read Version String (null-terminated UTF-16LE, e.g. "1.04" or "1.08")
        if (!TryReadNullTerminatedUtf16String(buffer, ref offset, bytesRead, out var versionString))
        {
            return OperationResult<ReplayMetadata>.CreateFailure("Unterminated UTF-16 version string in replay header.");
        }

        // 5. Read Build Time String (null-terminated UTF-16LE)
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

        // 9. Determine tick rate (FPS), total frames, duration, and match date
        var timingContext = new ReplayTimingContext(
            startTime,
            endTime,
            headerFrameCount,
            versionString,
            buildTimeString,
            titleString);

        var (totalFrames, fps, duration, timingGameDate) = ResolveTimingAndDuration(
            buffer,
            offset,
            bytesRead,
            timingContext);

        var metadata = new ReplayMetadata
        {
            VersionString = string.IsNullOrWhiteSpace(versionString) ? null : versionString,
            BuildTimeString = string.IsNullOrWhiteSpace(buildTimeString) ? null : buildTimeString,
            Title = string.IsNullOrWhiteSpace(titleString) ? null : titleString,
            VersionNumber = versionNumber,
            ExeCrc = exeCrc,
            IniCrc = iniCrc,
            GameDate = gameDate ?? timingGameDate,
            MapName = mapName,
            Players = players,
            Slots = slots,
            TotalFrames = totalFrames,
            FramesPerSecond = fps,
            Duration = duration,
        };

        return OperationResult<ReplayMetadata>.CreateSuccess(metadata);
    }

    private static DateTime? TryParseSystemTime(byte[] buffer, int offset)
    {
        try
        {
            var year = BitConverter.ToUInt16(buffer, offset);
            var month = BitConverter.ToUInt16(buffer, offset + 2);
            var day = BitConverter.ToUInt16(buffer, offset + 6);
            var hour = BitConverter.ToUInt16(buffer, offset + 8);
            var minute = BitConverter.ToUInt16(buffer, offset + 10);
            var second = BitConverter.ToUInt16(buffer, offset + 12);
            var millisecond = BitConverter.ToUInt16(buffer, offset + 14);

            if (year >= 1990 && year <= 2100 && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month) &&
                hour <= 23 && minute <= 59 && second <= 59 && millisecond <= 999)
            {
                return new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Unspecified);
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            // Keep null if values form an invalid date
        }

        return null;
    }

    private static (uint? TotalFrames, int? Fps, TimeSpan? Duration, DateTime? GameDate) ResolveTimingAndDuration(
        byte[] buffer,
        int offsetAfterInitString,
        int bytesRead,
        in ReplayTimingContext ctx)
    {
        var is60Hz = (ctx.VersionString?.Contains(ReplayManagerConstants.HighRefreshRateKeyword, StringComparison.OrdinalIgnoreCase) == true) ||
                     (ctx.VersionString?.Contains(PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase) == true) ||
                     (ctx.BuildTimeString?.Contains(ReplayManagerConstants.HighRefreshRateKeyword, StringComparison.OrdinalIgnoreCase) == true) ||
                     (ctx.TitleString?.Contains(ReplayManagerConstants.HighRefreshRateKeyword, StringComparison.OrdinalIgnoreCase) == true);
        var baseFps = is60Hz ? ReplayManagerConstants.GeneralsOnlineFps : ReplayManagerConstants.ClassicFps;

        uint? totalFrames = ctx.HeaderFrameCount > 0 ? ctx.HeaderFrameCount : null;
        TimeSpan? duration = null;
        int? fps = null;

        if (totalFrames.HasValue && totalFrames.Value > 0)
        {
            fps = baseFps;
            duration = TimeSpan.FromSeconds((double)totalFrames.Value / baseFps);
        }
        else if (ctx.EndTime > ctx.StartTime && ctx.StartTime >= ReplayManagerConstants.MinSanityTimestampEpoch)
        {
            var seconds = ctx.EndTime - ctx.StartTime;
            const long maxSanityDurationSeconds = 86400; // 24 hours
            if (seconds > 0 && seconds <= maxSanityDurationSeconds)
            {
                duration = TimeSpan.FromSeconds(seconds);
                fps = baseFps;
                var calculatedFrames = (double)seconds * baseFps;
                if (calculatedFrames <= uint.MaxValue)
                {
                    totalFrames = (uint)Math.Round(calculatedFrames);
                }
            }
        }
        else
        {
            var scanOffset = offsetAfterInitString;
            if (TryReadNullTerminatedAsciiString(buffer, ref scanOffset, bytesRead, out _))
            {
                // Engine writes null-terminated local player index string (e.g. "0 " or "-1 "),
                // followed by a 16-byte fixed trailer before the chunk stream.
                scanOffset += ReplayManagerConstants.ReplayPlayerIndexFixedTrailerSizeBytes;
            }
            else
            {
                scanOffset = offsetAfterInitString + ReplayManagerConstants.ReplayPostHeaderTrailerSizeBytes;
            }

            if (scanOffset < bytesRead)
            {
                var scannedFrames = TryScanMaxChunkTimecode(buffer, scanOffset, bytesRead);
                if (scannedFrames.HasValue && scannedFrames.Value > 0)
                {
                    totalFrames = scannedFrames;
                    fps = baseFps;
                    duration = TimeSpan.FromSeconds((double)scannedFrames.Value / baseFps);
                }
            }
        }

        DateTime? gameDate = null;
        if (ctx.StartTime >= ReplayManagerConstants.MinSanityTimestampEpoch)
        {
            gameDate = DateTimeOffset.FromUnixTimeSeconds(ctx.StartTime).UtcDateTime;
        }

        return (totalFrames, fps, duration, gameDate);
    }

    private static uint? TryScanMaxChunkTimecode(byte[] buffer, int offset, int bytesRead)
    {
        uint maxTimecode = 0;
        var cur = offset;

        // Each chunk header contains timecode (4), command (4), number (4), ncomms (1) = 13 bytes
        while (cur + ReplayManagerConstants.MaxChunkTimecodeStrideBytes <= bytesRead)
        {
            var timecode = BitConverter.ToUInt32(buffer, cur);
            if (timecode is 0xFFFFFFFF or 0x7FFFFFFF)
            {
                break;
            }

            var next = SkipChunkPayload(buffer, cur, bytesRead);
            if (!next.HasValue)
            {
                break;
            }

            if (timecode > maxTimecode)
            {
                maxTimecode = timecode;
            }

            cur = next.Value;
        }

        return maxTimecode > 0 ? maxTimecode : null;
    }

    private static int? SkipChunkPayload(byte[] buffer, int cur, int bytesRead)
    {
        var ncomms = buffer[cur + ReplayManagerConstants.MaxChunkTimecodeStrideBytes - 1];
        cur += ReplayManagerConstants.MaxChunkTimecodeStrideBytes;

        var descriptorBytes = ncomms * 2;
        if (cur + descriptorBytes > bytesRead)
        {
            return null;
        }

        var payloadBytes = 0;
        for (var i = 0; i < ncomms; i++)
        {
            var type = buffer[cur + (i * 2)];
            var nargs = buffer[cur + (i * 2) + 1];
            var argSize = GetCommandArgSize(type);
            if (argSize < 0)
            {
                return null;
            }

            payloadBytes += nargs * argSize;
        }

        var next = cur + descriptorBytes + payloadBytes;
        return next <= bytesRead ? next : null;
    }

    private static int GetCommandArgSize(byte cmdType) => cmdType switch
    {
        0x0 => ReplayManagerConstants.CommandArgSizes.Integer,
        0x1 => ReplayManagerConstants.CommandArgSizes.Real,
        0x2 => ReplayManagerConstants.CommandArgSizes.Boolean,
        0x3 => ReplayManagerConstants.CommandArgSizes.ObjectId,
        0x4 => ReplayManagerConstants.CommandArgSizes.DrawableId,
        0x5 => ReplayManagerConstants.CommandArgSizes.TeamId,
        0x6 => ReplayManagerConstants.CommandArgSizes.Location,
        0x7 => ReplayManagerConstants.CommandArgSizes.Pixel,
        0x8 => ReplayManagerConstants.CommandArgSizes.PixelRegion,
        0x9 => ReplayManagerConstants.CommandArgSizes.Timestamp,
        0xA => ReplayManagerConstants.CommandArgSizes.WideChar,
        _ => -1,
    };

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
                value = length > 0 ? Encoding.Latin1.GetString(buffer, start, length) : string.Empty;
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
        var slots = slotData.Split(':', StringSplitOptions.TrimEntries);
        for (var i = 0; i < slots.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(slots[i]))
            {
                continue;
            }

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
        var rawMarkerAndName = parts[0];
        var cleaned = CleanPlayerName(rawMarkerAndName, isSlotDefinition);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        playerName = cleaned;
        if (isSlotDefinition)
        {
            var isAi = rawMarkerAndName.Length > 0 && rawMarkerAndName[0] == 'C';
            var isHuman = !isAi;
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
            return trimmed[0] is 'H' or 'C' or 'X' or 'O' ? string.Empty : trimmed;
        }

        // In C&C Generals wire format, slot entries in S= prepend uppercase 'H' (Human) or 'C' (Computer) to the player name.
        if (trimmed[0] == 'H')
        {
            return trimmed[1..].Trim();
        }

        if (trimmed[0] == 'C')
        {
            var diff = trimmed[1..].Trim();
            return diff switch
            {
                "E" => "AI (Easy)",
                "M" => "AI (Medium)",
                "H" => "AI (Hard)",
                _ => diff,
            };
        }

        return trimmed;
    }
}
