namespace GenHub.Core.Constants;

/// <summary>
/// Constants for the Texture Editor tool and shared SAGE texture services.
/// </summary>
public static class TextureEditorConstants
{
    /// <summary>
    /// Tool identifier for Texture Editor.
    /// </summary>
    public const string ToolId = "texture-editor";

    /// <summary>
    /// Tool display name for Texture Editor.
    /// </summary>
    public const string ToolName = "Texture Editor";

    /// <summary>
    /// Tool description for Texture Editor.
    /// </summary>
    public const string ToolDescription = "Slice texture atlases, edit MappedImages visually, and pack loose icons into SAGE sheets.";

    /// <summary>
    /// Icon URI for Texture Editor tool.
    /// </summary>
    public const string IconPath = UriConstants.TextureEditorIconUri;

    /// <summary>
    /// Root SAGE directory for MappedImages INI files, relative to game data.
    /// </summary>
    public const string MappedImagesRootDirectory = "Data/INI/MappedImages";

    /// <summary>
    /// Directory name for hand-authored MappedImages overrides, applied last in SAGE load order.
    /// </summary>
    public const string HandCreatedDirectoryName = "HandCreated";

    /// <summary>
    /// Directory name prefix for per-texture-size MappedImages overrides, applied before HandCreated.
    /// </summary>
    public const string TextureSizeDirectoryPrefix = "TextureSize_";

    /// <summary>
    /// INI block keyword for a mapped image entry.
    /// </summary>
    public const string IniBlockName = "MappedImage";

    /// <summary>
    /// INI block terminator keyword.
    /// </summary>
    public const string IniBlockEnd = "End";

    /// <summary>
    /// Default status value written for MappedImage entries.
    /// </summary>
    public const string DefaultStatus = "NONE";

    /// <summary>
    /// Maximum texture dimension in pixels supported by SAGE Direct3D 8 atlases.
    /// </summary>
    public const int MaxTextureDimension = 2048;

    /// <summary>
    /// Default padding in pixels between packed sprites to prevent texture bleed.
    /// </summary>
    public const int DefaultPadding = 1;

    /// <summary>
    /// Standard large cameo width in pixels.
    /// </summary>
    public const int CameoLargeWidth = 64;

    /// <summary>
    /// Standard large cameo height in pixels.
    /// </summary>
    public const int CameoLargeHeight = 64;

    /// <summary>
    /// Standard small cameo width in pixels.
    /// </summary>
    public const int CameoSmallWidth = 60;

    /// <summary>
    /// Standard small cameo height in pixels.
    /// </summary>
    public const int CameoSmallHeight = 48;

    /// <summary>
    /// Standard HUD button width in pixels.
    /// </summary>
    public const int HudButtonWidth = 32;

    /// <summary>
    /// Standard HUD button height in pixels.
    /// </summary>
    public const int HudButtonHeight = 32;

    /// <summary>
    /// Inspector size preset key for large 64x64 cameos.
    /// </summary>
    public const string PresetLargeCameo = "64x64";

    /// <summary>
    /// Inspector size preset key for small 60x48 cameos.
    /// </summary>
    public const string PresetSmallCameo = "60x48";

    /// <summary>
    /// Inspector size preset key for 32x32 HUD buttons.
    /// </summary>
    public const string PresetHudButton = "32x32";

    /// <summary>
    /// Inspector size preset key for square 128x128 slices.
    /// </summary>
    public const string Preset128 = "128x128";

    /// <summary>
    /// Inspector size preset key for square 256x256 slices.
    /// </summary>
    public const string Preset256 = "256x256";

    /// <summary>
    /// Inspector size preset key stretching the slice to the atlas right edge.
    /// </summary>
    public const string PresetFillX = "fill-x";

    /// <summary>
    /// Inspector size preset key stretching the slice to the atlas bottom edge.
    /// </summary>
    public const string PresetFillY = "fill-y";

    /// <summary>
    /// Inspector size preset key stretching the slice to the atlas corner.
    /// </summary>
    public const string PresetFill = "fill";

    /// <summary>
    /// Square edge length in pixels for the 128 preset.
    /// </summary>
    public const int PresetMediumSize = 128;

    /// <summary>
    /// Square edge length in pixels for the 256 preset.
    /// </summary>
    public const int PresetLargeSize = 256;

    /// <summary>
    /// Item type identifier for texture atlas build rules in ModBuilder project files.
    /// </summary>
    public const string ModBuilderItemType = "TextureAtlas";

    /// <summary>
    /// File pattern for MappedImages INI files.
    /// </summary>
    public const string MappedImagesFilePattern = "*.ini";

    /// <summary>
    /// File name of the texture sheet written by auto-pack into the source directory.
    /// </summary>
    public const string PackedAtlasTextureFileName = "PackedAtlas.tga";

    /// <summary>
    /// File name of the mapped images INI written by auto-pack into the source directory.
    /// </summary>
    public const string PackedAtlasIniFileName = "PackedAtlas.ini";

    /// <summary>
    /// Mapped images INI file extension.
    /// </summary>
    public const string MappedImagesExtension = ".ini";

    /// <summary>
    /// Suffix appended to duplicated slice names.
    /// </summary>
    public const string DuplicateNameSuffix = "_Copy";

    /// <summary>
    /// Pixel offset applied to pasted or duplicated slices so they do not stack exactly.
    /// </summary>
    public const int PasteOffset = 16;

    /// <summary>
    /// Supported texture file extensions for the editor.
    /// </summary>
    public static readonly string[] TextureExtensions = [".tga", ".dds", ".png"];

    /// <summary>
    /// Image extensions accepted as auto-pack sources, covering every format the image loader decodes.
    /// </summary>
    public static readonly string[] PackableSourceExtensions = [".tga", ".dds", ".png", ".bmp"];

    /// <summary>
    /// File search patterns listed in the texture editor file explorer.
    /// </summary>
    public static readonly string[] ExplorerFilePatterns = ["*.tga", "*.dds", "*.png", "*.ini"];

    /// <summary>
    /// Match timeout guarding Coords parsing against pathological input lines.
    /// </summary>
    public static readonly TimeSpan CoordsRegexTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Valid power-of-two atlas dimensions.
    /// </summary>
    public static readonly int[] PowerOfTwoSizes = [32, 64, 128, 256, 512, 1024, 2048];
}
