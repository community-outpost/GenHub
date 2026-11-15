using GenHub.Core.Constants;

namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Render state shader.
/// </summary>
/// <param name="DepthCompare">The depth comparison function.</param>
/// <param name="DepthMask">Whether depth writes are enabled.</param>
/// <param name="DestBlend">The destination blend function.</param>
/// <param name="SrcBlend">The source blend function.</param>
/// <param name="Texturing">Whether texturing is enabled.</param>
/// <param name="AlphaTest">Whether alpha testing is enabled.</param>
public sealed record W3dShader(byte DepthCompare, byte DepthMask, byte DestBlend, byte SrcBlend, byte Texturing, byte AlphaTest)
{
    /// <summary>
    /// Gets a value indicating whether the shader enables texturing.
    /// </summary>
    public bool EnablesTexturing => Texturing != W3dConstants.ShaderValues.TexturingDisable;

    /// <summary>
    /// Gets a value indicating whether the shader blends with the frame buffer.
    /// </summary>
    public bool EnablesBlending => SrcBlend != W3dConstants.ShaderValues.SrcBlendOne || DestBlend != W3dConstants.ShaderValues.DestBlendZero;
}
