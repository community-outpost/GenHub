using Avalonia;
using FluentAssertions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Tools.WndEditor;
using GenHub.Features.Tools.WndEditor.Services;
using GenHub.Features.Tools.WndEditor.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.ViewModels;

/// <summary>
/// Unit tests for <see cref="WndEditorViewModel"/>.
/// </summary>
public sealed class WndEditorViewModelTests : IDisposable
{
    private const string SampleDocument =
        "FILE_VERSION = 2;\n" +
        "WINDOW\n" +
        "  WINDOWTYPE = USER;\n" +
        "  SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: 800 600, CREATIONRESOLUTION: 800 600;\n" +
        "  NAME = \"Menu.wnd:Parent\";\n" +
        "  CHILD\n" +
        "  WINDOW\n" +
        "    WINDOWTYPE = PUSHBUTTON;\n" +
        "    SCREENRECT = UPPERLEFT: 10 20, BOTTOMRIGHT: 110 60, CREATIONRESOLUTION: 800 600;\n" +
        "  END\n" +
        "  ENDALLCHILDREN\n" +
        "END\n";

    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly Mock<ILocalizationService> _mockLocalizationService;
    private readonly Mock<IDialogService> _mockDialogService;
    private readonly WndEditorViewModel _viewModel;
    private readonly string _tempDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="WndEditorViewModelTests"/> class.
    /// </summary>
    public WndEditorViewModelTests()
    {
        _mockNotificationService = new Mock<INotificationService>();
        _mockLocalizationService = new Mock<ILocalizationService>();
        _mockDialogService = new Mock<IDialogService>();
        _mockLocalizationService
            .Setup(s => s.GetString(It.IsAny<string>(), It.IsAny<object?[]>()))
            .Returns((string key, object?[] args) => key);
        _mockDialogService
            .Setup(s => s.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(true);
        var documentService = new WndDocumentService(Mock.Of<ILogger<WndDocumentService>>());
        _viewModel = new WndEditorViewModel(
            documentService,
            _mockNotificationService.Object,
            _mockLocalizationService.Object,
            _mockDialogService.Object,
            Mock.Of<ILogger<WndEditorViewModel>>());
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <summary>
    /// Cleans up the temporary directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Tests that loading valid content builds the tree, canvas, and selection.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LoadFromText_ValidContent_BuildsTreeCanvasAndProperties()
    {
        // Act
        var loaded = await _viewModel.LoadFromTextAsync(SampleDocument, null);

        // Assert
        loaded.Should().BeTrue();
        _viewModel.HasDocument.Should().BeTrue();
        _viewModel.RootNodes.Should().ContainSingle();
        _viewModel.RootNodes[0].Children.Should().ContainSingle();
        _viewModel.CanvasItems.Should().HaveCount(2);
        _viewModel.SelectedNode = _viewModel.RootNodes[0];
        _viewModel.SelectedProperties.Should().NotBeNull();
        _viewModel.SelectedProperties!.ShortName.Should().Be("Parent");
        _viewModel.SelectedProperties.SelectedWindowType.Should().Be("USER");
        _viewModel.IsModified.Should().BeFalse();
    }

    /// <summary>
    /// Tests that loading invalid content reports an error.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LoadFromText_InvalidContent_ReturnsFalseAndShowsError()
    {
        // Act
        var loaded = await _viewModel.LoadFromTextAsync("WINDOW\n  WINDOWTYPE = USER\nEND\n", null);

        // Assert
        loaded.Should().BeFalse();
        _viewModel.HasDocument.Should().BeFalse();
        _mockNotificationService.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that editing a property commits an undoable change.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task PropertyEdit_CommitsUndoableChange()
    {
        // Arrange
        await _viewModel.LoadFromTextAsync(SampleDocument, null);
        _viewModel.SelectedNode = _viewModel.RootNodes[0];

        // Act
        _viewModel.SelectedProperties!.ShortName = "Renamed";

        // Assert
        _viewModel.SelectedNode!.Window.GetProperty("NAME").Should().Be("\"Menu.wnd:Renamed\"");
        _viewModel.CanUndo.Should().BeTrue();
        _viewModel.IsModified.Should().BeTrue();

        // Act
        _viewModel.UndoCommand.Execute(null);

        // Assert
        _viewModel.SelectedNode!.Window.GetProperty("NAME").Should().Be("\"Menu.wnd:Parent\"");
        _viewModel.CanRedo.Should().BeTrue();
    }

    /// <summary>
    /// Tests that redo reapplies an undone edit.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Redo_ReappliesUndoneEdit()
    {
        // Arrange
        await _viewModel.LoadFromTextAsync(SampleDocument, null);
        _viewModel.SelectedNode = _viewModel.RootNodes[0];
        _viewModel.SelectedProperties!.ShortName = "Renamed";
        _viewModel.UndoCommand.Execute(null);

        // Act
        _viewModel.RedoCommand.Execute(null);

        // Assert
        _viewModel.SelectedNode!.Window.GetProperty("NAME").Should().Be("\"Menu.wnd:Renamed\"");
        _viewModel.CanUndo.Should().BeTrue();
        _viewModel.CanRedo.Should().BeFalse();
    }

    /// <summary>
    /// Tests that adding and deleting a child window updates the tree.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task AddDelete_ChildWindow_UpdatesTree()
    {
        // Arrange
        await _viewModel.LoadFromTextAsync(SampleDocument, null);
        _viewModel.SelectedNode = _viewModel.RootNodes[0];

        // Act
        _viewModel.AddChildWindowCommand.Execute(null);

        // Assert
        _viewModel.RootNodes[0].Children.Should().HaveCount(2);
        _viewModel.SelectedNode.Should().Be(_viewModel.RootNodes[0].Children[1]);

        // Act
        _viewModel.DeleteSelectedWindowCommand.Execute(null);

        // Assert
        _viewModel.RootNodes[0].Children.Should().ContainSingle();

        // Act
        _viewModel.UndoCommand.Execute(null);

        // Assert
        _viewModel.RootNodes[0].Children.Should().HaveCount(2);
    }

    /// <summary>
    /// Tests that adding a duplicate property warns without changing the document.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task AddProperty_DuplicateKey_ShowsWarning()
    {
        // Arrange
        await _viewModel.LoadFromTextAsync(SampleDocument, null);
        _viewModel.SelectedNode = _viewModel.RootNodes[0];
        var count = _viewModel.SelectedNode!.Window.Properties.Count;
        _viewModel.SelectedProperties!.NewPropertyKey = "NAME";
        _viewModel.SelectedProperties.NewPropertyValue = "Other";

        // Act
        _viewModel.SelectedProperties.AddPropertyCommand.Execute(null);

        // Assert
        _viewModel.SelectedNode!.Window.Properties.Should().HaveCount(count);
        _mockNotificationService.Verify(
            n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that dragging a canvas item moves the window and records one undo step.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CanvasDrag_MovesWindowAndRecordsUndo()
    {
        // Arrange
        await _viewModel.LoadFromTextAsync(SampleDocument, null);
        var item = _viewModel.CanvasItems.First(i => i.Window.ControlType == WndControlType.PushButton);

        // Act
        _viewModel.BeginCanvasDrag(item, new Point(10, 20));
        _viewModel.UpdateCanvasDrag(new Point(30, 50));
        _viewModel.EndCanvasDrag();

        // Assert
        item.Window.TryGetScreenRect(out var moved).Should().BeTrue();
        moved.Should().Be(new WndScreenRect(30, 50, 130, 90, 800, 600));
        _viewModel.CanUndo.Should().BeTrue();

        // Act
        _viewModel.UndoCommand.Execute(null);

        // Assert
        item.Window.TryGetScreenRect(out var restored).Should().BeTrue();
        restored.Should().Be(new WndScreenRect(10, 20, 110, 60, 800, 600));
    }

    /// <summary>
    /// Tests that saving writes canonical text to the file.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SaveFile_WritesCanonicalText()
    {
        // Arrange
        var path = Path.Combine(_tempDirectory, "Saved.wnd");
        await _viewModel.LoadFromTextAsync("WINDOW\nWINDOWTYPE=USER;\nEND\n", path);
        _viewModel.SelectedNode = _viewModel.RootNodes[0];
        _viewModel.SelectedProperties!.SelectedWindowType = "STATICTEXT";

        // Act
        await _viewModel.SaveFileCommand.ExecuteAsync(null);

        // Assert
        var saved = await File.ReadAllTextAsync(path);
        saved.Should().Be("FILE_VERSION = 2;\nWINDOW\n  WINDOWTYPE = STATICTEXT;\nEND\n");
        _viewModel.IsModified.Should().BeFalse();
    }

    /// <summary>
    /// Tests that opening a missing file reports an error.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task OpenFile_MissingFile_ReturnsFalse()
    {
        // Act
        var opened = await _viewModel.OpenFileAsync(Path.Combine(_tempDirectory, "missing.wnd"));

        // Assert
        opened.Should().BeFalse();
        _mockNotificationService.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that zoom clamps to the supported range.
    /// </summary>
    [Fact]
    public void Zoom_ClampsToRange()
    {
        // Act
        _viewModel.Zoom = 5.0;

        // Assert
        _viewModel.Zoom.Should().Be(2.0);

        // Act
        _viewModel.Zoom = 0.01;

        // Assert
        _viewModel.Zoom.Should().Be(0.25);
    }

    /// <summary>
    /// Tests that a new document contains one default window.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task NewDocument_CreatesDefaultWindow()
    {
        // Act
        await _viewModel.NewDocumentCommand.ExecuteAsync(null);

        // Assert
        _viewModel.HasDocument.Should().BeTrue();
        _viewModel.RootNodes.Should().ContainSingle();
        _viewModel.CanvasItems.Should().ContainSingle();
        _viewModel.IsModified.Should().BeTrue();
    }

    /// <summary>
    /// Tests that validating a valid document shows success.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ValidateDocument_Valid_ShowsSuccess()
    {
        // Arrange
        await _viewModel.LoadFromTextAsync(SampleDocument, null);

        // Act
        _viewModel.ValidateDocumentCommand.Execute(null);

        // Assert
        _mockNotificationService.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that toggling a status flag commits an undoable change.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task StatusFlag_Toggle_CommitsUndoableChange()
    {
        // Arrange
        await _viewModel.LoadFromTextAsync(SampleDocument, null);
        _viewModel.SelectedNode = _viewModel.RootNodes[0];
        var flag = _viewModel.SelectedProperties!.BasicStatusFlags.First(f => f.Name == "ENABLED");

        // Act
        flag.IsChecked = true;

        // Assert
        _viewModel.SelectedNode!.Window.GetProperty("STATUS").Should().Be("ENABLED");
        _viewModel.CanUndo.Should().BeTrue();

        // Act
        _viewModel.UndoCommand.Execute(null);

        // Assert
        _viewModel.SelectedNode!.Window.GetProperty("STATUS").Should().BeNull();
    }

    /// <summary>
    /// Tests that editing a position component commits an undoable change.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Position_Edit_CommitsUndoableChange()
    {
        // Arrange
        await _viewModel.LoadFromTextAsync(SampleDocument, null);
        _viewModel.SelectedNode = _viewModel.RootNodes[0];

        // Act
        _viewModel.SelectedProperties!.UpperLeftX = 50;

        // Assert
        _viewModel.SelectedNode!.Window.GetProperty("SCREENRECT").Should().Be(
            "UPPERLEFT: 50 0, BOTTOMRIGHT: 800 600, CREATIONRESOLUTION: 800 600");

        // Act
        _viewModel.UndoCommand.Execute(null);

        // Assert
        _viewModel.SelectedNode!.Window.GetProperty("SCREENRECT").Should().Be(
            "UPPERLEFT: 0 0, BOTTOMRIGHT: 800 600, CREATIONRESOLUTION: 800 600");
    }

    /// <summary>
    /// Tests that applying raw text replaces all properties with undo support.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task RawText_Apply_ReplacesProperties()
    {
        // Arrange
        await _viewModel.LoadFromTextAsync(SampleDocument, null);
        _viewModel.SelectedNode = _viewModel.RootNodes[0];
        var properties = _viewModel.SelectedProperties!;
        properties.RawText = "WINDOWTYPE = STATICTEXT;\nNAME = \"Menu.wnd:Raw\";";

        // Act
        properties.ApplyRawTextCommand.Execute(null);

        // Assert
        _viewModel.SelectedNode!.Window.GetProperty("NAME").Should().Be("\"Menu.wnd:Raw\"");
        _viewModel.SelectedNode!.Window.ControlType.Should().Be(WndControlType.StaticText);

        // Act
        _viewModel.UndoCommand.Execute(null);

        // Assert
        _viewModel.SelectedNode!.Window.GetProperty("NAME").Should().Be("\"Menu.wnd:Parent\"");
        _viewModel.SelectedNode!.Window.ControlType.Should().Be(WndControlType.User);
    }

    /// <summary>
    /// Tests that applying invalid raw text reports an error without changing the window.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task RawText_Invalid_ShowsError()
    {
        // Arrange
        await _viewModel.LoadFromTextAsync(SampleDocument, null);
        _viewModel.SelectedNode = _viewModel.RootNodes[0];
        var properties = _viewModel.SelectedProperties!;
        properties.RawText = "WINDOWTYPE = USER";

        // Act
        properties.ApplyRawTextCommand.Execute(null);

        // Assert
        _viewModel.SelectedNode!.Window.GetProperty("NAME").Should().Be("\"Menu.wnd:Parent\"");
        _mockNotificationService.Verify(
            n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that the window filter narrows the tree to matches and their ancestors.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task WindowsFilter_Text_NarrowsTree()
    {
        // Arrange
        await _viewModel.LoadFromTextAsync(SampleDocument, null);

        // Act
        _viewModel.WindowsFilter = "pushbutton";

        // Assert
        _viewModel.RootNodes.Should().ContainSingle();
        _viewModel.RootNodes[0].Children.Should().ContainSingle();

        // Act
        _viewModel.WindowsFilter = "nothing-matches-this";

        // Assert
        _viewModel.RootNodes.Should().BeEmpty();

        // Act
        _viewModel.WindowsFilter = string.Empty;

        // Assert
        _viewModel.RootNodes.Should().ContainSingle();
        _viewModel.RootNodes[0].Children.Should().ContainSingle();
    }

    /// <summary>
    /// Tests that opening a file lists sibling files in the explorer.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task OpenFile_ListsSiblingsInExplorer()
    {
        // Arrange
        var firstPath = Path.Combine(_tempDirectory, "First.wnd");
        var secondPath = Path.Combine(_tempDirectory, "Second.wnd");
        await File.WriteAllTextAsync(firstPath, SampleDocument);
        await File.WriteAllTextAsync(secondPath, SampleDocument);

        // Act
        var opened = await _viewModel.OpenFileAsync(firstPath);

        // Assert
        opened.Should().BeTrue();
        _viewModel.Files.Should().HaveCount(2);
        _viewModel.Files.Should().ContainSingle(f => f.IsCurrent).Which.FileName.Should().Be("First.wnd");

        // Act
        await _viewModel.OpenExplorerFileCommand.ExecuteAsync(_viewModel.Files.First(f => !f.IsCurrent));

        // Assert
        _viewModel.FilePath.Should().Be(secondPath);
        _viewModel.Files.Should().ContainSingle(f => f.IsCurrent).Which.FileName.Should().Be("Second.wnd");
    }

    /// <summary>
    /// Tests that control data editors follow the selected control type.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ControlData_FollowsSelectedControlType()
    {
        // Arrange
        const string SliderDocument =
            "FILE_VERSION = 2;\n" +
            "WINDOW\n" +
            "  WINDOWTYPE = HORZSLIDER;\n" +
            "  SLIDERDATA = MINVALUE: 0, MAXVALUE: 100;\n" +
            "END\n";
        await _viewModel.LoadFromTextAsync(SliderDocument, null);
        _viewModel.SelectedNode = _viewModel.RootNodes[0];

        // Assert
        var properties = _viewModel.SelectedProperties!;
        properties.IsSlider.Should().BeTrue();
        properties.IsControlDataSupported.Should().BeTrue();
        properties.HasControlData.Should().BeTrue();
        properties.SliderMaxValue.Should().Be(100);

        // Act
        properties.SliderMaxValue = 200;

        // Assert
        _viewModel.SelectedNode!.Window.GetProperty("SLIDERDATA").Should().Be("MINVALUE: 0, MAXVALUE: 200");
    }

    /// <summary>
    /// Tests that draw data entries commit the whole set.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DrawData_Edit_CommitsSet()
    {
        // Arrange
        await _viewModel.LoadFromTextAsync(SampleDocument, null);
        _viewModel.SelectedNode = _viewModel.RootNodes[0];
        var properties = _viewModel.SelectedProperties!;

        // Assert
        properties.EnabledDrawData.Should().HaveCount(9);

        // Act
        properties.EnabledDrawData[0].Image = "Circle_Small03_Black";

        // Assert
        var parsed = WndDrawDataSet.TryParse(
            _viewModel.SelectedNode!.Window.GetProperty("ENABLEDDRAWDATA"),
            out var set);
        parsed.Should().BeTrue();
        set!.Entries[0].Image.Should().Be("Circle_Small03_Black");
    }

    /// <summary>
    /// Tests that tree node IsHidden recognizes the hidden status flag case-insensitively.
    /// </summary>
    /// <param name="status">The status value under test.</param>
    /// <param name="expectedIsHidden">Whether the node is expected to be marked hidden.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData("HIDDEN", true)]
    [InlineData("hidden", true)]
    [InlineData("Hidden", true)]
    [InlineData("ENABLED+HIDDEN", true)]
    [InlineData("ENABLED+hidden", true)]
    [InlineData("ENABLED", false)]
    [InlineData("NULL", false)]
    public async Task TreeNode_IsHidden_IsCaseInsensitive(string status, bool expectedIsHidden)
    {
        // Arrange
        var doc =
            "FILE_VERSION = 2;\n" +
            "WINDOW\n" +
            "  WINDOWTYPE = USER;\n" +
            $"  STATUS = {status};\n" +
            "END\n";
        await _viewModel.LoadFromTextAsync(doc, null);

        // Assert
        _viewModel.RootNodes.Should().ContainSingle();
        _viewModel.RootNodes[0].IsHidden.Should().Be(expectedIsHidden);
    }
}
