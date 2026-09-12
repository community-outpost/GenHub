namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Represents file types supported by the ModBuilder conversion system.
/// </summary>
public enum BuildFileType
{
    /// <summary>
    /// Generals .big archive format.
    /// </summary>
    Big,

    /// <summary>
    /// Blender 3D model file (.blend).
    /// </summary>
    Blend,

    /// <summary>
    /// Bitmap image file (.bmp).
    /// </summary>
    Bmp,

    /// <summary>
    /// Compiled String File - game string table (.csf).
    /// </summary>
    Csf,

    /// <summary>
    /// DirectDraw Surface texture file (.dds).
    /// </summary>
    Dds,

    /// <summary>
    /// Gzip compressed archive (.gz).
    /// </summary>
    Gz,

    /// <summary>
    /// INI configuration file (.ini).
    /// </summary>
    Ini,

    /// <summary>
    /// Photoshop document (.psd).
    /// </summary>
    Psd,

    /// <summary>
    /// String table text file (.str).
    /// </summary>
    Str,

    /// <summary>
    /// Tar archive file (.tar).
    /// </summary>
    Tar,

    /// <summary>
    /// Targa image file (.tga).
    /// </summary>
    Tga,

    /// <summary>
    /// Tagged Image File Format (.tiff).
    /// </summary>
    Tiff,

    /// <summary>
    /// Westwood 3D model file (.w3d).
    /// </summary>
    W3d,

    /// <summary>
    /// Window definition file (.wnd).
    /// </summary>
    Wnd,

    /// <summary>
    /// ZIP archive file (.zip).
    /// </summary>
    Zip,

    /// <summary>
    /// Matches any file type.
    /// </summary>
    Any,

    /// <summary>
    /// Automatically determine file type from extension.
    /// </summary>
    Auto,
}
