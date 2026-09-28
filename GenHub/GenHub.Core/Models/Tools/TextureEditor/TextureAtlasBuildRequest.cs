using GenHub.Core.Constants;
using System.Diagnostics.CodeAnalysis;

namespace GenHub.Core.Models.Tools.TextureEditor;

/// <summary>
/// Represents a ModBuilder asset rule that synthesizes a texture atlas from loose source images.
/// This is the shared contract consumed by the ModBuilder build pipeline so texture packing
/// stays in one place instead of being reimplemented per tool.
/// </summary>
/// <param name="SourceDirectory">The directory holding loose source images.</param>
/// <param name="TargetTexture">The output atlas path, for example Art/Textures/CustomCameos_1024.tga.</param>
/// <param name="TargetIni">The output MappedImages INI path.</param>
/// <param name="GenerateMipmaps">Whether to generate mipmaps. SAGE 2D UI textures require false.</param>
/// <param name="Padding">The padding in pixels between packed sprites.</param>
public sealed record TextureAtlasBuildRequest(
    string SourceDirectory,
    string TargetTexture,
    string TargetIni,
    bool GenerateMipmaps = false,
    int Padding = TextureEditorConstants.DefaultPadding)
{
    /// <summary>
    /// Gets the item type discriminator used in ModBuilder project files.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Discriminator must stay an instance member for ModBuilder project dispatch and serialization.")]
    public string ItemType => TextureEditorConstants.ModBuilderItemType;
}
