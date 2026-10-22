using System;

namespace GenHub.Features.Tools.TextureEditor.ViewModels;

/// <summary>
/// A single undoable edit in the Texture Editor.
/// </summary>
/// <param name="Description">Human-readable description of the edit.</param>
/// <param name="Redo">Applies the edit.</param>
/// <param name="Undo">Reverts the edit.</param>
public sealed record TextureEditAction(string Description, Action Redo, Action Undo);
