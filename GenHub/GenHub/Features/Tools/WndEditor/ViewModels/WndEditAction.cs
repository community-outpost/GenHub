using System;

namespace GenHub.Features.Tools.WndEditor.ViewModels;

/// <summary>
/// A single undoable edit in the WND editor.
/// </summary>
/// <param name="Description">Human-readable description of the edit.</param>
/// <param name="Redo">Applies the edit.</param>
/// <param name="Undo">Reverts the edit.</param>
public sealed record WndEditAction(string Description, Action Redo, Action Undo);
