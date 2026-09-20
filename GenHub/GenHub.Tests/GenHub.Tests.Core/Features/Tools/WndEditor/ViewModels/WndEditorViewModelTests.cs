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
        _viewModel.PropertyRows.Should().NotBeEmpty();
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
        var row = _viewModel.PropertyRows.First(r => r.Key == "NAME");

        // Act
        row.Value = "\"Menu.wnd:Renamed\"";

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
        _viewModel.PropertyRows.First(r => r.Key == "NAME").Value = "\"Menu.wnd:Renamed\"";
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
        _viewModel.NewPropertyKey = "NAME";
        _viewModel.NewPropertyValue = "Other";

        // Act
        _viewModel.AddPropertyCommand.Execute(null);

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
        _viewModel.PropertyRows.First(r => r.Key == "WINDOWTYPE").Value = "STATICTEXT";

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
}
