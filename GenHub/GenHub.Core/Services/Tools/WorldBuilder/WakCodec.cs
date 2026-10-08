// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Encodes and decodes .wak wave-track files: 20-byte records
/// (float startX, float startY, float endX, float endY, int32 waveType) followed by
/// a 4-byte little-endian int32 record count trailer.
/// </summary>
/// <remarks>
/// Verified against WaterTracksRenderSystem::saveTracksTo/loadTracksFrom
/// (W3DWaterTracks.cpp): each record writes the start Vector2, the end Vector2,
/// then the waveType enum (an Int), and the file ends with the track count.
/// Only primary wave fronts (init time offset zero, excluding the editor preview
/// track) are persisted; second-layer waves are rebuilt automatically on load.
/// </remarks>
public static class WakCodec
{
    /// <summary>Record size in bytes.</summary>
    public const int RecordSize = 20;

    /// <summary>
    /// Encodes tracks to .wak bytes.
    /// </summary>
    /// <param name="tracks">The tracks.</param>
    /// <returns>The file bytes.</returns>
    public static byte[] Encode(IReadOnlyList<WaveTrackRecord> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        using var output = new MemoryStream((tracks.Count * RecordSize) + 4);
        foreach (var track in tracks)
        {
            output.Write(BitConverter.GetBytes(track.StartX), 0, 4);
            output.Write(BitConverter.GetBytes(track.StartY), 0, 4);
            output.Write(BitConverter.GetBytes(track.EndX), 0, 4);
            output.Write(BitConverter.GetBytes(track.EndY), 0, 4);
            output.Write(BitConverter.GetBytes(track.WaveType), 0, 4);
        }

        output.Write(BitConverter.GetBytes(tracks.Count), 0, 4);
        return output.ToArray();
    }

    /// <summary>
    /// Decodes .wak bytes to tracks.
    /// </summary>
    /// <param name="data">The file bytes.</param>
    /// <returns>The tracks.</returns>
    public static OperationResult<IReadOnlyList<WaveTrackRecord>> Decode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 4 || ((data.Length - 4) % RecordSize) != 0)
        {
            return OperationResult<IReadOnlyList<WaveTrackRecord>>.CreateFailure("Truncated .wak file.");
        }

        var count = BitConverter.ToInt32(data, data.Length - 4);
        if (count < 0 || count != (data.Length - 4) / RecordSize)
        {
            return OperationResult<IReadOnlyList<WaveTrackRecord>>.CreateFailure("Invalid .wak track count.");
        }

        var tracks = new List<WaveTrackRecord>();
        for (var i = 0; i < count; i++)
        {
            var offset = i * RecordSize;
            tracks.Add(new WaveTrackRecord
            {
                StartX = BitConverter.ToSingle(data, offset),
                StartY = BitConverter.ToSingle(data, offset + 4),
                EndX = BitConverter.ToSingle(data, offset + 8),
                EndY = BitConverter.ToSingle(data, offset + 12),
                WaveType = BitConverter.ToInt32(data, offset + 16),
            });
        }

        return OperationResult<IReadOnlyList<WaveTrackRecord>>.CreateSuccess(tracks);
    }
}
