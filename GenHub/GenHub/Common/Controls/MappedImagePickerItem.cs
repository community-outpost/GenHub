using Avalonia.Media;
using GenHub.Core.Models.Tools.TextureEditor;

namespace GenHub.Common.Controls;

/// <summary>
/// Represents one row in the <see cref="MappedImagePickerControl"/>.
/// </summary>
/// <param name="Definition">The mapped image entry.</param>
/// <param name="Thumbnail">The optional cropped sprite thumbnail.</param>
public sealed record MappedImagePickerItem(MappedImageDefinition Definition, IImage? Thumbnail);
