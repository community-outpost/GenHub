using System;

namespace GenHub.Features.Tools.IniEditor.ViewModels;

/// <summary>
/// A single undoable edit in the INI editor.
/// </summary>
/// <param name="Title">The display title of the edit.</param>
/// <param name="Redo">Applies the edit.</param>
/// <param name="Undo">Reverts the edit.</param>
/// <param name="CoalesceKey">Optional key merging consecutive edits of the same target into one undo step.</param>
public sealed record IniEditAction(string Title, Action Redo, Action Undo, object? CoalesceKey = null);
