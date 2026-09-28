using GenHub.Core.Constants;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.TextureEditor;

namespace GenHub.Core.Interfaces.Tools.TextureEditor;

/// <summary>
/// Packs loose sprites into power-of-two texture atlases and synthesizes ModBuilder artifacts.
/// </summary>
public interface IAtlasPackingService
{
    /// <summary>
    /// Computes sprite placements inside a power-of-two sheet.
    /// </summary>
    /// <param name="sources">The source images to pack.</param>
    /// <param name="padding">The padding in pixels between sprites.</param>
    /// <param name="maxDimension">The maximum sheet dimension in pixels.</param>
    /// <returns>The pack geometry, or a failure describing the problem.</returns>
    OperationResult<AtlasPackResult> Pack(IReadOnlyList<AtlasSourceImage> sources, int padding = TextureEditorConstants.DefaultPadding, int maxDimension = TextureEditorConstants.MaxTextureDimension);

    /// <summary>
    /// Blits source pixels into a sheet following computed placements.
    /// </summary>
    /// <param name="sources">The source images.</param>
    /// <param name="pack">The pack geometry.</param>
    /// <returns>The composed sheet, or a failure describing the problem.</returns>
    OperationResult<DecodedTexture> ComposeSheet(IReadOnlyList<AtlasSourceImage> sources, AtlasPackResult pack);

    /// <summary>
    /// Executes a ModBuilder texture atlas build rule end to end.
    /// </summary>
    /// <param name="request">The build rule.</param>
    /// <param name="loader">The host image loader.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The build artifacts, or a failure describing the problem.</returns>
    Task<OperationResult<TextureAtlasBuildResult>> BuildAtlasAsync(
        TextureAtlasBuildRequest request,
        ITextureImageLoader loader,
        CancellationToken cancellationToken = default);
}
