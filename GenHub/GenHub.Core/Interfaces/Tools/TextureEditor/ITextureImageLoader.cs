using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.TextureEditor;

namespace GenHub.Core.Interfaces.Tools.TextureEditor;

/// <summary>
/// Loads source images for atlas builds. Implemented by the host application so
/// core packing stays free of UI framework imaging dependencies.
/// </summary>
public interface ITextureImageLoader
{
    /// <summary>
    /// Loads and decodes a source image file.
    /// </summary>
    /// <param name="path">The image file path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The decoded source image, or a failure describing the problem.</returns>
    Task<OperationResult<AtlasSourceImage>> LoadAsync(string path, CancellationToken cancellationToken = default);
}
