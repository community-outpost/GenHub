using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for <see cref="MapUndoService"/> and <see cref="MapMemento"/>.
/// </summary>
public sealed class MapUndoServiceTests
{
    /// <summary>
    /// Tests that undo restores terrain edits.
    /// </summary>
    [Fact]
    public void Undo_RestoresTerrain()
    {
        var map = CreateMap();
        var undo = new MapUndoService();
        undo.Checkpoint(map);
        map.Terrain.Heights[0] = 99;

        var undone = undo.Undo(map);

        Assert.True(undone);
        Assert.Equal(20, map.Terrain.Heights[0]);
    }

    /// <summary>
    /// Tests that redo reapplies undone edits.
    /// </summary>
    [Fact]
    public void Redo_ReappliesEdit()
    {
        var map = CreateMap();
        var undo = new MapUndoService();
        undo.Checkpoint(map);
        map.Terrain.Heights[0] = 99;
        undo.Undo(map);

        var redone = undo.Redo(map);

        Assert.True(redone);
        Assert.Equal(99, map.Terrain.Heights[0]);
    }

    /// <summary>
    /// Tests that undo restores object edits.
    /// </summary>
    [Fact]
    public void Undo_RestoresObjects()
    {
        var map = CreateMap();
        map.Objects.Add(new MapObjectEntry { X = 10, Y = 10, Name = "Tree" });
        var undo = new MapUndoService();
        undo.Checkpoint(map);
        map.Objects.Clear();

        undo.Undo(map);

        var restored = Assert.Single(map.Objects);
        Assert.Equal("Tree", restored.Name);
    }

    /// <summary>
    /// Tests that a new checkpoint discards redo history.
    /// </summary>
    [Fact]
    public void Checkpoint_ClearsRedo()
    {
        var map = CreateMap();
        var undo = new MapUndoService();
        undo.Checkpoint(map);
        undo.Undo(map);
        Assert.True(undo.CanRedo);

        undo.Checkpoint(map);

        Assert.False(undo.CanRedo);
    }

    /// <summary>
    /// Tests that undo on an empty stack returns false.
    /// </summary>
    [Fact]
    public void Undo_Empty_ReturnsFalse()
    {
        Assert.False(new MapUndoService().Undo(CreateMap()));
    }

    /// <summary>
    /// Tests that undo restores dimensions and texture classes across a document swap.
    /// </summary>
    [Fact]
    public void Undo_RestoresDimensionsAndTextureClasses()
    {
        var map = CreateMap();
        map.Terrain.BorderSize = 2;
        map.Terrain.Boundaries.Add(new MapBoundary(2, 2));
        map.Terrain.TextureClasses.Add(new MapTextureClass(0, 64, 8, "Grass"));
        map.Terrain.NumBitmapTiles = 64;
        var undo = new MapUndoService();
        undo.Checkpoint(map);

        map.Terrain.Width = 8;
        map.Terrain.Height = 8;
        map.Terrain.Heights = new byte[64];
        map.Terrain.TileIndices = new short[64];
        map.Terrain.BlendTileIndices = new short[64];
        map.Terrain.ExtraBlendTileIndices = new short[64];
        map.Terrain.CliffInfoIndices = new short[64];
        map.Terrain.CliffState = new byte[8];
        map.Terrain.TextureClasses.Add(new MapTextureClass(64, 64, 8, "Cliff"));
        map.Terrain.NumBitmapTiles = 128;

        var undone = undo.Undo(map);

        Assert.True(undone);
        Assert.Equal(4, map.Terrain.Width);
        Assert.Equal(4, map.Terrain.Height);
        Assert.Equal(2, map.Terrain.BorderSize);
        Assert.Equal(64, map.Terrain.NumBitmapTiles);
        var textureClass = Assert.Single(map.Terrain.TextureClasses);
        Assert.Equal("Grass", textureClass.Name);
        Assert.Equal(16, map.Terrain.Heights.Count);
    }

    /// <summary>
    /// Tests that checkpointing marks the live document dirty.
    /// </summary>
    [Fact]
    public void Checkpoint_MarksMapDirty()
    {
        var map = CreateMap();
        map.IsDirty = false;

        new MapUndoService().Checkpoint(map);

        Assert.True(map.IsDirty);
    }

    /// <summary>
    /// Tests that undo restores the pre-edit dirty flag.
    /// </summary>
    [Fact]
    public void Undo_RestoresDirtyFlag()
    {
        var map = CreateMap();
        map.IsDirty = false;
        var undo = new MapUndoService();
        undo.Checkpoint(map);
        map.Terrain.Heights[0] = 99;

        var undone = undo.Undo(map);

        Assert.True(undone);
        Assert.False(map.IsDirty);
        Assert.Equal(20, map.Terrain.Heights[0]);
    }

    private static WorldBuilderMap CreateMap()
    {
        var map = new WorldBuilderMap();
        map.Terrain.Width = 4;
        map.Terrain.Height = 4;
        var heights = new byte[16];
        Array.Fill(heights, (byte)20);
        map.Terrain.Heights = heights;
        map.Terrain.TileIndices = new short[16];
        map.Terrain.BlendTileIndices = new short[16];
        map.Terrain.ExtraBlendTileIndices = new short[16];
        map.Terrain.CliffInfoIndices = new short[16];
        map.Terrain.CliffState = new byte[16];
        return map;
    }
}
