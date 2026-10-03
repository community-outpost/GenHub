using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenHub.Common.Controls;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.IniEditor;
using GenHub.Core.Interfaces.Tools.ModelViewer;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Interfaces.Tools.WndEditor;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.IniEditor;
using GenHub.Features.Tools.IniEditor.Services;
using GenHub.Features.Tools.IniEditor.ViewModels;
using GenHub.Features.Tools.IniEditor.Views;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static GenHub.Tests.Core.Features.Tools.IniEditor.IniEditorTestFactory;

namespace GenHub.Tests.Core.Features.Tools.IniEditor.Views;

/// <summary>
/// Headless layout tests for <see cref="IniEditorView"/>.
/// </summary>
public class IniEditorViewTests
{
    /// <summary>
    /// Verifies that the editor renders its three panes and populates the block tree from a document.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task EditorView_WithDocument_RendersPanesAndBlockTreeAsync()
    {
        using var viewModel = CreateViewModel();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Object";
        viewModel.NewBlockName = "TestObject";
        viewModel.AddBlockCommand.Execute(null);

        Assert.Single(viewModel.RootNodes);

        var view = new IniEditorView { DataContext = viewModel };
        var window = new Window { Width = 1600, Height = 900, Content = view };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs(null);

            var treeViews = view.GetVisualDescendants().OfType<TreeView>().ToList();
            Assert.NotEmpty(treeViews);

            var textBoxes = view.GetVisualDescendants().OfType<TextBox>().ToList();
            Assert.NotEmpty(textBoxes);

            var buttons = view.GetVisualDescendants().OfType<Button>().ToList();
            Assert.NotEmpty(buttons);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that the empty state renders open actions when no document is loaded.
    /// </summary>
    [AvaloniaFact]
    public void EditorView_WithoutDocument_RendersEmptyState()
    {
        using var viewModel = CreateViewModel();

        var view = new IniEditorView { DataContext = viewModel };
        var window = new Window { Width = 1600, Height = 900, Content = view };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs(null);

            var buttons = view.GetVisualDescendants().OfType<Button>().ToList();
            Assert.NotEmpty(buttons);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that deleting the selected block clears the selection and a second delete does not throw.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task DeleteCommand_ClearsSelectionAndFieldRowsAsync()
    {
        using var viewModel = CreateViewModel();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Object";
        viewModel.NewBlockName = "Victim";
        viewModel.AddBlockCommand.Execute(null);
        Assert.NotNull(viewModel.SelectedNode);

        viewModel.DeleteCommand.Execute(null);

        Assert.Null(viewModel.SelectedNode);
        Assert.Empty(viewModel.FieldRows);
        Assert.Empty(viewModel.RootNodes);

        var exception = Record.Exception(() => viewModel.DeleteCommand.Execute(null));
        Assert.Null(exception);
    }

    /// <summary>
    /// Verifies that undo removes added blocks and redo re-inserts them in position.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task UndoRedo_AddBlock_RestoresAndReappliesInPositionAsync()
    {
        using var viewModel = CreateViewModel();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Object";
        viewModel.NewBlockName = "First";
        viewModel.AddBlockCommand.Execute(null);
        viewModel.NewBlockName = "Second";
        viewModel.AddBlockCommand.Execute(null);
        Assert.Equal(2, viewModel.RootNodes.Count);

        viewModel.UndoCommand.Execute(null);

        Assert.Single(viewModel.RootNodes);
        Assert.Equal("First", viewModel.RootNodes[0].Block.Name);

        viewModel.RedoCommand.Execute(null);

        Assert.Equal(2, viewModel.RootNodes.Count);
        Assert.Equal("First", viewModel.RootNodes[0].Block.Name);
        Assert.Equal("Second", viewModel.RootNodes[1].Block.Name);
    }

    /// <summary>
    /// Verifies that editing a field value is undoable and redoable.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task FieldEdit_UndoRedo_RestoresValueAsync()
    {
        using var viewModel = CreateViewModel();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Object";
        viewModel.NewBlockName = "Edited";
        viewModel.AddBlockCommand.Execute(null);

        viewModel.FieldRows.First(row => row.Key == "Health").Value = "42.0";
        Assert.Equal("42.0", viewModel.FieldRows.First(row => row.Key == "Health").Value);

        viewModel.UndoCommand.Execute(null);
        Assert.Equal("100.0", viewModel.FieldRows.First(row => row.Key == "Health").Value);

        viewModel.RedoCommand.Execute(null);
        Assert.Equal("42.0", viewModel.FieldRows.First(row => row.Key == "Health").Value);
    }

    /// <summary>
    /// Verifies that undo restores the exact repeated-key row that was edited.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task FieldEdit_RepeatedKey_UndoRestoresEditedRowAsync()
    {
        using var viewModel = CreateViewModel();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "WeaponSet";
        viewModel.NewBlockName = "Set";
        viewModel.AddBlockCommand.Execute(null);

        viewModel.NewFieldKey = "Weapon";
        viewModel.NewFieldValue = "PRIMARY WeaponA";
        viewModel.AddFieldCommand.Execute(null);
        viewModel.NewFieldKey = "Weapon";
        viewModel.NewFieldValue = "SECONDARY WeaponB";
        viewModel.AddFieldCommand.Execute(null);

        var rows = viewModel.FieldRows.Where(row => row.Key == "Weapon").ToList();
        Assert.Equal(2, rows.Count);

        rows[1].Value = "TERTIARY WeaponC";

        viewModel.UndoCommand.Execute(null);

        rows = viewModel.FieldRows.Where(row => row.Key == "Weapon").ToList();
        Assert.Equal("PRIMARY WeaponA", rows[0].Value);
        Assert.Equal("SECONDARY WeaponB", rows[1].Value);

        viewModel.RedoCommand.Execute(null);

        rows = viewModel.FieldRows.Where(row => row.Key == "Weapon").ToList();
        Assert.Equal("TERTIARY WeaponC", rows[1].Value);
    }

    /// <summary>
    /// Verifies that consecutive edits to a field preserve the original value on undo and final value on redo.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task FieldEdit_ConsecutiveEdits_UndoRestoresOriginalValueAsync()
    {
        using var viewModel = CreateViewModel();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Object";
        viewModel.NewBlockName = "Edited";
        viewModel.AddBlockCommand.Execute(null);

        viewModel.FieldRows.First(row => row.Key == "Health").Value = "150.0";
        Assert.Equal("150.0", viewModel.FieldRows.First(row => row.Key == "Health").Value);
        viewModel.FieldRows.First(row => row.Key == "Health").Value = "200.0";

        Assert.Equal("200.0", viewModel.FieldRows.First(row => row.Key == "Health").Value);

        viewModel.UndoCommand.Execute(null);
        Assert.Equal("100.0", viewModel.FieldRows.First(row => row.Key == "Health").Value);

        viewModel.RedoCommand.Execute(null);
        Assert.Equal("200.0", viewModel.FieldRows.First(row => row.Key == "Health").Value);
    }

    /// <summary>
    /// Verifies that clearing a valued field empties it first and deletes it once empty.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task ClearOrDeleteField_ValueSet_ClearsThenDeletesAsync()
    {
        using var viewModel = CreateViewModel();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Object";
        viewModel.NewBlockName = "Edited";
        viewModel.AddBlockCommand.Execute(null);

        viewModel.NewFieldKey = "Side";
        viewModel.NewFieldValue = "GDI";
        viewModel.AddFieldCommand.Execute(null);

        int before = viewModel.FieldRows.Count(row => row.Key == "Side");
        var row = viewModel.FieldRows.Last(row => row.Key == "Side");
        viewModel.ClearOrDeleteFieldCommand.Execute(row);

        Assert.Equal(string.Empty, row.Value);
        Assert.Equal(before, viewModel.FieldRows.Count(row => row.Key == "Side"));

        viewModel.ClearOrDeleteFieldCommand.Execute(row);
        Assert.Equal(before - 1, viewModel.FieldRows.Count(row => row.Key == "Side"));
    }

    /// <summary>
    /// Verifies that duplicate block names are rejected.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task AddBlock_DuplicateName_IsRejectedAsync()
    {
        using var viewModel = CreateViewModel();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Object";
        viewModel.NewBlockName = "Duplicate";
        viewModel.AddBlockCommand.Execute(null);
        viewModel.NewBlockName = "Duplicate";
        viewModel.AddBlockCommand.Execute(null);

        Assert.Single(viewModel.RootNodes);
    }

    /// <summary>
    /// Verifies that empty block names are rejected except for blocks that allow them.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task AddBlock_EmptyName_IsRejectedExceptWhenAllowedAsync()
    {
        using var viewModel = CreateViewModel();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Object";
        viewModel.NewBlockName = string.Empty;
        viewModel.AddBlockCommand.Execute(null);

        Assert.Empty(viewModel.RootNodes);

        viewModel.NewBlockType = "ExperienceLevels";
        viewModel.AddBlockCommand.Execute(null);

        Assert.Single(viewModel.RootNodes);
    }

    /// <summary>
    /// Verifies that template insertions skip fields the block already defines.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task AddUpgradeHookup_ExistingFields_AreSkippedAsync()
    {
        using var viewModel = CreateViewModel();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Object";
        viewModel.NewBlockName = "Hooked";
        viewModel.AddBlockCommand.Execute(null);
        viewModel.AddUpgradeHookupCommand.Execute(null);
        var fieldCount = viewModel.SelectedNode!.Block.Fields.Count;

        viewModel.AddUpgradeHookupCommand.Execute(null);

        Assert.Equal(fieldCount, viewModel.SelectedNode!.Block.Fields.Count);
        Assert.Single(viewModel.SelectedNode.Block.Fields, field => field.Key == "Upgrade");
    }

    /// <summary>
    /// Verifies that opening a flat settings file populates editable file-settings rows
    /// and that edits round-trip through save.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task OpenFile_FlatSettings_PopulatesGlobalFieldRowsAsync()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"GenHubIniGlobals{Guid.NewGuid():N}.ini");
        await File.WriteAllTextAsync(filePath, "ReallyLowMHz = 600\nLODPreset = LOW P3 1400 GF3 128\n");
        try
        {
            using var viewModel = CreateViewModel();
            var opened = await viewModel.OpenFileAsync(filePath);

            Assert.True(opened);
            Assert.True(viewModel.HasGlobalFields);
            Assert.Equal(2, viewModel.GlobalFieldRows.Count);
            Assert.Equal("ReallyLowMHz", viewModel.GlobalFieldRows[0].Key);

            viewModel.GlobalFieldRows[0].Value = "700";
            await viewModel.SaveCommand.ExecuteAsync(null);

            var saved = await File.ReadAllTextAsync(filePath);
            Assert.Contains("ReallyLowMHz = 700", saved, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// Verifies that opening an INI file while the editor view is bound and attached to a visual tree
    /// does not crash with a dispatcher thread access violation on command refresh.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task OpenFileAsync_WithAttachedVisualTree_DoesNotThrowThreadAccessViolationAsync()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"GenHubThreadCheck{Guid.NewGuid():N}.ini");
        await File.WriteAllTextAsync(filePath, "Object TestObject\n  Health = 100.0\nEnd\n");
        try
        {
            using var viewModel = CreateViewModel();
            var view = new IniEditorView { DataContext = viewModel };
            var window = new Window { Width = 1600, Height = 900, Content = view };

            window.Show();
            Dispatcher.UIThread.RunJobs(null);

            var opened = await viewModel.OpenFileAsync(filePath);
            Dispatcher.UIThread.RunJobs(null);

            Assert.True(opened);
            Assert.True(viewModel.HasDocument);
            Assert.Single(viewModel.RootNodes);
            Assert.Equal("TestObject", viewModel.RootNodes[0].Block.Name);

            window.Close();
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// Verifies that OpenFolderAsync populates the file explorer, opens the first INI file,
    /// and activates the Files tab.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task OpenFolderAsync_WithIniFiles_LoadsFirstFileAndSelectsFilesTabAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"GenHubFolderTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var subDir = Path.Combine(tempDir, "Sub");
        Directory.CreateDirectory(subDir);
        var firstIni = Path.Combine(subDir, "First.ini");
        await File.WriteAllTextAsync(firstIni, "Object FirstObject\n  Health = 50.0\nEnd\n");

        try
        {
            using var viewModel = CreateViewModel();
            var opened = await viewModel.OpenFolderAsync(tempDir);
            Dispatcher.UIThread.RunJobs(null);

            Assert.True(opened);
            Assert.True(viewModel.HasDocument);
            Assert.Equal(1, viewModel.LeftSidebarTabIndex);
            Assert.Equal(tempDir, viewModel.FileExplorer.Directory);
            Assert.Equal(firstIni, viewModel.FileExplorer.CurrentPath);
            Assert.Single(viewModel.RootNodes);
            Assert.Equal("FirstObject", viewModel.RootNodes[0].Block.Name);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Verifies that OpenFolderAsync without INI files sets the explorer directory,
    /// activates the Files tab, and displays an informative notification.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task OpenFolderAsync_WithoutIniFiles_ShowsInfoNotificationAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"GenHubEmptyFolderTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var txtFile = Path.Combine(tempDir, "Readme.txt");
        await File.WriteAllTextAsync(txtFile, "Not an ini file");

        var mockNotifications = new Mock<INotificationService>();

        try
        {
            using var viewModel = CreateViewModel(mockNotifications.Object);
            var opened = await viewModel.OpenFolderAsync(tempDir);
            Dispatcher.UIThread.RunJobs(null);

            Assert.True(opened);
            Assert.False(viewModel.HasDocument);
            Assert.Equal(1, viewModel.LeftSidebarTabIndex);
            Assert.Equal(tempDir, viewModel.FileExplorer.Directory);

            mockNotifications.Verify(
                n => n.ShowInfo(
                    It.Is<string>(s => s.Contains("NoIniFilesTitle", StringComparison.OrdinalIgnoreCase) || s.Contains("INI", StringComparison.OrdinalIgnoreCase)),
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<bool>()),
                Times.Once);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Verifies that opening a file inside an already-opened directory preserves
    /// the directory root in the file explorer.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task OpenFile_WithinActiveFolder_PreservesExplorerRootDirectoryAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"GenHubPreserveRoot_{Guid.NewGuid():N}");
        var subDir = Path.Combine(tempDir, "Data", "INI");
        Directory.CreateDirectory(subDir);
        var file1 = Path.Combine(subDir, "File1.ini");
        var file2 = Path.Combine(subDir, "File2.ini");
        await File.WriteAllTextAsync(file1, "Object Object1\nEnd\n");
        await File.WriteAllTextAsync(file2, "Object Object2\nEnd\n");

        try
        {
            using var viewModel = CreateViewModel();
            await viewModel.OpenFolderAsync(tempDir);
            Dispatcher.UIThread.RunJobs(null);

            Assert.Equal(tempDir, viewModel.FileExplorer.Directory);

            await viewModel.OpenFileAsync(file2);
            Dispatcher.UIThread.RunJobs(null);

            Assert.Equal(tempDir, viewModel.FileExplorer.Directory);
            Assert.Equal(file2, viewModel.FileExplorer.CurrentPath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Verifies that opening a file with blocks auto-selects the first block so the
    /// properties panel is populated instead of showing an empty sidebar.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task OpenFile_WithBlocks_AutoSelectsFirstBlockAndPopulatesFieldsAsync()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"GenHubAutoSelect{Guid.NewGuid():N}.ini");
        await File.WriteAllTextAsync(filePath, "Object FirstObject\n  Health = 100.0\nEnd\nObject SecondObject\n  Health = 50.0\nEnd\n");
        try
        {
            using var viewModel = CreateViewModel();
            var opened = await viewModel.OpenFileAsync(filePath);

            Assert.True(opened);
            Assert.NotNull(viewModel.SelectedNode);
            Assert.False(viewModel.SelectedNode.IsGroupHeader);
            Assert.Equal("FirstObject", viewModel.SelectedNode.Block.Name);
            Assert.NotEmpty(viewModel.FieldRows);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// Verifies that toggling block grouping with a live view attached preserves the
    /// editable selection and its fields instead of clearing the properties panel.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task GroupedDocument_GroupToggle_PreservesSelectionAndFieldsAsync()
    {
        using var viewModel = CreateViewModel();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        for (var i = 0; i < 14; i++)
        {
            viewModel.NewBlockType = "Object";
            viewModel.NewBlockName = $"Obj{i}";
            viewModel.AddBlockCommand.Execute(null);
        }

        viewModel.NewFieldKey = "Health";
        viewModel.NewFieldValue = "100.0";
        viewModel.AddFieldCommand.Execute(null);

        var header = viewModel.VisibleRootNodes.First(node => node.IsGroupHeader);
        header.IsExpanded = true;
        var nested = header.Children[0];
        viewModel.SelectedNode = nested;

        Assert.NotEmpty(viewModel.FieldRows);

        var view = new IniEditorView { DataContext = viewModel };
        var window = new Window { Width = 1600, Height = 900, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs(null);

            viewModel.IsGroupByTypeEnabled = false;
            Dispatcher.UIThread.RunJobs(null);

            viewModel.IsGroupByTypeEnabled = true;
            Dispatcher.UIThread.RunJobs(null);

            Assert.NotNull(viewModel.SelectedNode);
            Assert.False(viewModel.SelectedNode.IsGroupHeader);
            Assert.NotEmpty(viewModel.FieldRows);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that the properties panel renders field cards in a two-column grid
    /// with realized side-by-side items.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task PropertiesPanel_RendersFieldsInTwoColumnsAsync()
    {
        using var viewModel = CreateViewModel();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Object";
        viewModel.NewBlockName = "TwoColumn";
        viewModel.AddBlockCommand.Execute(null);
        viewModel.NewFieldKey = "Health";
        viewModel.NewFieldValue = "100.0";
        viewModel.AddFieldCommand.Execute(null);
        viewModel.NewFieldKey = "BuildCost";
        viewModel.NewFieldValue = "500";
        viewModel.AddFieldCommand.Execute(null);

        Assert.True(viewModel.FieldRows.Count >= 2);

        var view = new IniEditorView { DataContext = viewModel };
        var window = new Window { Width = 1600, Height = 900, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs(null);

            var fieldPanel = view.GetVisualDescendants()
                .OfType<ItemsControl>()
                .FirstOrDefault(control => ReferenceEquals(control.ItemsSource, viewModel.FieldRows));

            Assert.NotNull(fieldPanel);
            Assert.IsType<TwoColumnPanel>(fieldPanel.ItemsPanelRoot);

            var first = Assert.IsAssignableFrom<Control>(fieldPanel.ContainerFromIndex(0));
            var second = Assert.IsAssignableFrom<Control>(fieldPanel.ContainerFromIndex(1));
            Assert.NotEqual(first.Bounds.X, second.Bounds.X);
            Assert.True(first.Bounds.Height < 56, $"Field card too tall for single-row density: {first.Bounds.Height}");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that selecting a child block keeps the parent object on the
    /// canvas while the fields panel follows the selection.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task SelectingChildBlock_KeepsObjectOnCanvasAsync()
    {
        using var viewModel = CreateViewModel();
        var filePath = Path.Combine(Path.GetTempPath(), $"GenHubCanvas{Guid.NewGuid():N}.ini");
        await File.WriteAllTextAsync(filePath, CanvasFixtureIni());
        try
        {
            Assert.True(await viewModel.OpenFileAsync(filePath));
            var state = viewModel.RootNodes[0].Children[0].Children[0];
            Assert.Equal("ModelConditionState", state.Block.BlockType);

            viewModel.SelectedNode = state;

            Assert.Equal("TestTank", viewModel.SelectedBlockTitle);
            Assert.Equal("Object", viewModel.SelectedBlockType);
            Assert.Equal(["Model", "ShowSubObjects"], viewModel.FieldRows.Select(row => row.Key).ToList());
            Assert.Equal(3, viewModel.SelectedNodeTrail.Count);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// Verifies that clearing the selection falls back to the first object on
    /// the canvas instead of leaving it empty.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task ClearedSelection_ShowsFirstObjectOnCanvasAsync()
    {
        using var viewModel = CreateViewModel();
        var filePath = Path.Combine(Path.GetTempPath(), $"GenHubCanvas{Guid.NewGuid():N}.ini");
        await File.WriteAllTextAsync(filePath, CanvasFixtureIni());
        try
        {
            Assert.True(await viewModel.OpenFileAsync(filePath));
            viewModel.SelectedNode = viewModel.RootNodes[0];
            Assert.Equal("TestTank", viewModel.SelectedBlockTitle);

            viewModel.SelectedNode = null;

            Assert.True(viewModel.HasSelectedBlock);
            Assert.Equal("TestTank", viewModel.SelectedBlockTitle);
            Assert.Equal("Object", viewModel.SelectedBlockType);
            Assert.Empty(viewModel.FieldRows);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// Verifies that the engine null audio marker does not raise an unknown
    /// reference warning while genuinely unknown audio still does.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task NoSoundReference_DoesNotWarnAsync()
    {
        var mockReferenceService = new Mock<IIniReferenceService>();
        mockReferenceService
            .Setup(service => service.RebuildIndexAsync(
                It.IsAny<IniDocument?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<int>.CreateSuccess(0, TimeSpan.Zero));
        mockReferenceService
            .Setup(service => service.Entries)
            .Returns(new List<IniReferenceEntry>());
        mockReferenceService
            .Setup(service => service.GetNames(It.IsAny<string>()))
            .Returns(new List<string> { "ExistingSound" });
        mockReferenceService
            .Setup(service => service.FindReferencersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<IniReferenceEntry>>.CreateSuccess(new List<IniReferenceEntry>(), TimeSpan.Zero));
        mockReferenceService
            .Setup(service => service.CloneBlockAsync(It.IsAny<IniReferenceEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IniBlock?>.CreateSuccess(null, TimeSpan.Zero));
        using var viewModel = CreateViewModel(null, null, mockReferenceService.Object);
        var filePath = Path.Combine(Path.GetTempPath(), $"GenHubNoSound{Guid.NewGuid():N}.ini");
        await File.WriteAllTextAsync(filePath, "Object TestInfantry\n  VoiceSelect = NoSound\n  VoiceMove = BogusSound\nEnd\n");
        try
        {
            Assert.True(await viewModel.OpenFileAsync(filePath));

            Assert.Equal(1, viewModel.ValidationIssueCount);
            var row = Assert.Single(viewModel.ValidationIssues);
            Assert.False(row.IsError);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static string CanvasFixtureIni()
    {
        return "Object TestTank\n" +
            "  Side = USA\n" +
            "  Draw = W3DTankDraw ModuleTag_01\n" +
            "    ModelConditionState = NONE\n" +
            "      Model = TestUnit\n" +
            "      ShowSubObjects = TURRET\n" +
            "    End\n" +
            "  End\n" +
            "End\n";
    }
}
