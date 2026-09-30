using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Model with its resolved textures.
/// </summary>
/// <param name="ModelName">The requested model name.</param>
/// <param name="SourceName">Where the model bytes were found.</param>
/// <param name="Model">The parsed model.</param>
/// <param name="Textures">The decoded textures keyed by referenced name.</param>
/// <param name="MissingTextures">The referenced texture names that could not be loaded.</param>
public sealed record W3dResolvedModel(
    string ModelName,
    string SourceName,
    W3dModel Model,
    IReadOnlyList<W3dResolvedTexture> Textures,
    IReadOnlyList<string> MissingTextures);
