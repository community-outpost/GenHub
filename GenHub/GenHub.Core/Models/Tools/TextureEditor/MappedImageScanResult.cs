namespace GenHub.Core.Models.Tools.TextureEditor;

/// <summary>
/// Represents the outcome of scanning INI files into a MappedImage registry.
/// </summary>
/// <param name="FilesScanned">The number of INI files parsed.</param>
/// <param name="ImagesIndexed">The number of MappedImage entries indexed.</param>
public sealed record MappedImageScanResult(int FilesScanned, int ImagesIndexed);
