using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;
using Microsoft.Extensions.Logging;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Reads and writes .wak wave-track companion files.
/// </summary>
public sealed class WaveTrackService(ILogger<WaveTrackService> logger) : IWaveTrackService
{
    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<WaveTrackRecord>>> LoadAsync(string wakPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(wakPath);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(wakPath))
            {
                return OperationResult<IReadOnlyList<WaveTrackRecord>>.CreateSuccess([]);
            }

            var bytes = await File.ReadAllBytesAsync(wakPath, cancellationToken).ConfigureAwait(false);
            return WakCodec.Decode(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to load waves from {WakPath}.", wakPath);
            return OperationResult<IReadOnlyList<WaveTrackRecord>>.CreateFailure($"Failed to load waves: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> SaveAsync(string wakPath, IReadOnlyList<WaveTrackRecord> tracks, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(wakPath);
        ArgumentNullException.ThrowIfNull(tracks);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = Path.GetDirectoryName(wakPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = $"{wakPath}.tmp.{Guid.NewGuid():N}";
            try
            {
                await File.WriteAllBytesAsync(tempPath, WakCodec.Encode(tracks), cancellationToken).ConfigureAwait(false);
                File.Move(tempPath, wakPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch (IOException)
                    {
                        // Clean up temp file if move failed
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Clean up temp file if move failed
                    }
                }
            }

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to save waves to {WakPath}.", wakPath);
            return OperationResult<bool>.CreateFailure($"Failed to save waves: {ex.Message}");
        }
    }
}
