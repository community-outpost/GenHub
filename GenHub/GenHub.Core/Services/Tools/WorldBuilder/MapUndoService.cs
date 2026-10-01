using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Bounded undo/redo stack for map editing sessions (CUndoable port).
/// </summary>
public sealed class MapUndoService
{
    private readonly LinkedList<MapMemento> undoStack = [];
    private readonly LinkedList<MapMemento> redoStack = [];

    /// <summary>Gets a value indicating whether undo is available.</summary>
    public bool CanUndo => undoStack.Count > 0;

    /// <summary>Gets a value indicating whether redo is available.</summary>
    public bool CanRedo => redoStack.Count > 0;

    /// <summary>Gets the number of stored undo steps.</summary>
    public int UndoCount => undoStack.Count;

    /// <summary>
    /// Captures the current state before an edit, discarding redo history.
    /// Marks the live document dirty: the memento already captured the
    /// pre-edit flag, so undo/redo restore the accurate dirty state.
    /// </summary>
    /// <param name="map">The map document.</param>
    public void Checkpoint(WorldBuilderMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        undoStack.AddLast(MapMemento.Capture(map));
        while (undoStack.Count > WorldBuilderConstants.Limits.MaxUndoDepth)
        {
            undoStack.RemoveFirst();
        }

        redoStack.Clear();
        map.IsDirty = true;
    }

    /// <summary>
    /// Restores the most recent checkpoint.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <returns>True when a step was undone.</returns>
    public bool Undo(WorldBuilderMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var node = undoStack.Last;
        if (node == null)
        {
            return false;
        }

        undoStack.RemoveLast();
        redoStack.AddLast(MapMemento.Capture(map));
        node.Value.Restore(map);
        return true;
    }

    /// <summary>
    /// Reapplies the most recently undone state.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <returns>True when a step was redone.</returns>
    public bool Redo(WorldBuilderMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var node = redoStack.Last;
        if (node == null)
        {
            return false;
        }

        redoStack.RemoveLast();
        undoStack.AddLast(MapMemento.Capture(map));
        node.Value.Restore(map);
        return true;
    }

    /// <summary>
    /// Discards all undo and redo history.
    /// </summary>
    public void Clear()
    {
        undoStack.Clear();
        redoStack.Clear();
    }
}
