using Avalonia.Media;
using GenHub.Core.Models.Tools.IniEditor;

namespace GenHub.Features.Tools.IniEditor.ViewModels;

/// <summary>
/// A command button icon shown on the preview strip, linking a command set
/// or button to the buttons grouped with it.
/// </summary>
/// <param name="Block">The underlying command button block.</param>
/// <param name="Label">The button display name.</param>
/// <param name="Icon">The button icon thumbnail, or null when unavailable.</param>
public sealed record PreviewCommandIconItem(IniBlock Block, string Label, IImage? Icon);
