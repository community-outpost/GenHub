using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.TextureEditor;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.TextureEditor.Services;

/// <summary>
/// Loads atlas source images for ModBuilder-style texture builds.
/// </summary>
public sealed class AvaloniaTextureImageLoader(TextureBitmapService bitmapService) : ITextureImageLoader
{
    /// <inheritdoc />
    public async Task<OperationResult<AtlasSourceImage>> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var decoded = await bitmapService.LoadDecodedAsync(path, cancellationToken).ConfigureAwait(false);
        if (decoded.Failed || decoded.Data is null)
        {
            return OperationResult<AtlasSourceImage>.CreateFailure(decoded, Stopwatch.GetElapsedTime(started));
        }

        string name = Path.GetFileNameWithoutExtension(path);
        return OperationResult<AtlasSourceImage>.CreateSuccess(new AtlasSourceImage(name, decoded.Data), Stopwatch.GetElapsedTime(started));
    }
}
