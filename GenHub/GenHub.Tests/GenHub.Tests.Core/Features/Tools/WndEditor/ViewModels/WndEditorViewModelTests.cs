using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.WndEditor;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WndEditor;
using GenHub.Features.Tools.WndEditor.Services;
using GenHub.Features.Tools.WndEditor.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
    private readonly Mock<IGameInstallationService> _mockGameInstallService;
    private readonly Mock<IWndImageAssetService> _mockImageAssetService;
    private readonly Mock<IWndStringTableService> _mockStringTableService;
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
        _mockGameInstallService = new Mock<IGameInstallationService>();
        _mockImageAssetService = new Mock<IWndImageAssetService>();
        _mockLocalizationService
            .Setup(s => s.GetString(It.IsAny<string>(), It.IsAny<object?[]>()))
            .Returns((string key, object?[] args) => key);
        _mockDialogService
            .Setup(s => s.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(true);
        _mockGameInstallService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([]));
        _mockImageAssetService
            .Setup(s => s.GetImagesAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<bool>()))
            .ReturnsAsync(OperationResult<IReadOnlyDictionary<string, byte[]>>.CreateSuccess(
                new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)));
        _mockStringTableService = new Mock<IWndStringTableService>();
        _mockStringTableService
            .Setup(s => s.GetStringsAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<bool>()))
            .ReturnsAsync(OperationResult<IReadOnlyDictionary<string, string>>.CreateSuccess(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));
        var documentService = new WndDocumentService(Mock.Of<ILogger<WndDocumentService>>());
        var assetService = new WndEditorAssetService(_mockImageAssetService.Object, _mockStringTableService.Object);
        _viewModel = new WndEditorViewModel(
            documentService,
            _mockNotificationService.Object,
            _mockLocalizationService.Object,
            _mockDialogService.Object,
            _mockGameInstallService.Object,
            assetService,
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
    /// Tests that resizing a canvas item updates the window screen rect and records an undo step.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CanvasResize_ResizesWindowAndRecordsUndo()
    {
        // Arrange
        await _viewModel.LoadFromTextAsync(SampleDocument, null);
        var item = _viewModel.CanvasItems.First(i => i.Window.ControlType == WndControlType.PushButton);

        // Act - resize SouthEast (drag corner by +20, +30)
        _viewModel.BeginCanvasResize(item, WndResizeDirection.SouthEast, new Point(110, 60));
        _viewModel.UpdateCanvasDrag(new Point(130, 90));
        _viewModel.EndCanvasDrag();

        // Assert
        item.Window.TryGetScreenRect(out var resized).Should().BeTrue();
        resized.Should().Be(new WndScreenRect(10, 20, 130, 90, 800, 600));
        _viewModel.CanUndo.Should().BeTrue();

        // Act - Undo
        _viewModel.UndoCommand.Execute(null);

        // Assert
        item.Window.TryGetScreenRect(out var restored).Should().BeTrue();
        restored.Should().Be(new WndScreenRect(10, 20, 110, 60, 800, 600));
    }

    /// <summary>
    /// Tests that custom font specified in the window is applied to the canvas item.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ContentFontFamily_AppliesWhenSpecifiedInWnd()
    {
        // Arrange
        const string fontDoc =
            "FILE_VERSION = 2;\n" +
            "WINDOW\n" +
            "  WINDOWTYPE = STATICTEXT;\n" +
            "  SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: 200 50, CREATIONRESOLUTION: 800 600;\n" +
            "  TEXT = \"Test\";\n" +
            "  FONT = NAME: \"Courier New\", SIZE: 14, BOLD: 0;\n" +
            "END\n";

        // Act
        await _viewModel.LoadFromTextAsync(fontDoc, null);

        // Assert
        var item = _viewModel.CanvasItems.Should().ContainSingle().Subject;
        item.ContentFontFamily.Should().NotBeNull();
        item.ContentFontFamily!.Name.Should().Be("Courier New");
    }

    /// <summary>
    /// Tests that linking mod folders and .big archives updates summary and properties.
    /// </summary>
    [Fact]
    public void LinkedAssets_ManagingModFolderAndBigFiles_UpdatesProperties()
    {
        // Assert initial
        _viewModel.HasLinkedAssets.Should().BeFalse();
        _viewModel.LinkedAssetsSummary.Should().BeEmpty();

        // Act - set linked mod folder and big archive
        _viewModel.LinkedModFolder = Path.Combine(_tempDirectory, "MyMod");
        _viewModel.LinkedBigFiles.Add(Path.Combine(_tempDirectory, "Textures.big"));
        _viewModel.GetType().GetMethod("UpdateLinkedAssetsSummary", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(_viewModel, null);

        // Assert
        _viewModel.HasLinkedAssets.Should().BeTrue();
        _viewModel.LinkedAssetsSummary.Should().Contain("MyMod");
        _viewModel.LinkedAssetsSummary.Should().Contain("1 .big");
        _viewModel.LinkedAssetsTooltip.Should().Contain("Textures.big");

        // Act - remove big file
        _viewModel.RemoveLinkedBigFileCommand.Execute(Path.Combine(_tempDirectory, "Textures.big"));

        // Assert
        _viewModel.LinkedBigFiles.Should().BeEmpty();
        _viewModel.HasLinkedAssets.Should().BeTrue();

        // Act - clear all
        _viewModel.ClearLinkedAssetsCommand.Execute(null);

        // Assert
        _viewModel.HasLinkedAssets.Should().BeFalse();
        _viewModel.LinkedModFolder.Should().BeNull();
        _viewModel.LinkedAssetsSummary.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that Sibling Generals path is discovered from parent directory.
    /// </summary>
    [Fact]
    public void FindSiblingGeneralsPath_DiscoversSiblingGeneralsDirectory()
    {
        // Arrange
        var root = Path.Combine(_tempDirectory, "SteamLibrary", "GeneralsZeroHour");
        var generalsDir = Path.Combine(root, "Command and Conquer Generals");
        var zhDir = Path.Combine(root, "Command and Conquer Generals Zero Hour");
        Directory.CreateDirectory(generalsDir);
        Directory.CreateDirectory(zhDir);

        // Act
        var method = typeof(WndEditorViewModel).GetMethod("FindSiblingGeneralsPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var result = (string?)method?.Invoke(null, [zhDir]);

        // Assert
        result.Should().Be(generalsDir);
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
        _viewModel.Files.Should().HaveCount(1);
        var root = _viewModel.Files[0];
        root.Children.Should().HaveCount(2);
        root.Children.Should().ContainSingle(f => f.IsCurrent).Which.FileName.Should().Be("First.wnd");

        // Act
        await _viewModel.OpenExplorerFileCommand.ExecuteAsync(root.Children.First(f => !f.IsCurrent));

        // Assert
        _viewModel.FilePath.Should().Be(secondPath);
        root.Children.Should().ContainSingle(f => f.IsCurrent).Which.FileName.Should().Be("Second.wnd");
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
        properties.EnabledDrawData.Should().HaveCount(WndConstants.DrawData.EntryCount);

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

    /// <summary>
    /// Tests that zoom commands step by the configured factor and clamp to range.
    /// </summary>
    [Fact]
    public void ZoomCommands_StepByFactor()
    {
        // Act
        _viewModel.ZoomInCommand.Execute(null);

        // Assert
        _viewModel.Zoom.Should().BeApproximately(WndConstants.Editor.DefaultZoom * WndConstants.Editor.ZoomStepFactor, 0.0001);

        // Act
        _viewModel.ZoomOutCommand.Execute(null);
        _viewModel.ZoomOutCommand.Execute(null);

        // Assert
        _viewModel.Zoom.Should().BeApproximately(WndConstants.Editor.DefaultZoom / WndConstants.Editor.ZoomStepFactor, 0.0001);

        // Act
        _viewModel.ResetZoomCommand.Execute(null);

        // Assert
        _viewModel.Zoom.Should().Be(WndConstants.Editor.DefaultZoom);
        _viewModel.ZoomDisplayText.Should().Be($"{WndConstants.Editor.DefaultZoom:P0}");
    }

    /// <summary>
    /// Tests that pan mode is off by default and switches the canvas cursor to a hand.
    /// </summary>
    [AvaloniaFact]
    public void PanMode_TogglesHandCursor()
    {
        // Assert
        _viewModel.IsPanMode.Should().BeFalse();
        _viewModel.CanvasCursor.Should().BeNull();

        // Act
        _viewModel.IsPanMode = true;

        // Assert
        _viewModel.CanvasCursor.Should().NotBeNull();
        _viewModel.CanvasCursor!.ToString().Should().Be(new Cursor(StandardCursorType.Hand).ToString());
    }

    /// <summary>
    /// Tests that installations populate asset options and prefer Zero Hour.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Installations_Populate_PreferringZeroHour()
    {
        // Arrange
        var generalsDir = Path.Combine(_tempDirectory, "Generals");
        var zeroHourDir = Path.Combine(_tempDirectory, "ZeroHour");
        Directory.CreateDirectory(generalsDir);
        Directory.CreateDirectory(zeroHourDir);
        var installation = new GameInstallation(_tempDirectory, GameInstallationType.Steam)
        {
            HasGenerals = true,
            GeneralsPath = generalsDir,
            HasZeroHour = true,
            ZeroHourPath = zeroHourDir,
        };
        _mockGameInstallService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([installation]));

        // Act
        await _viewModel.LoadFromTextAsync(SampleDocument, null);

        // Assert
        _viewModel.AvailableInstallations.Should().HaveCount(2);
        _viewModel.SelectedAssetInstallation.Should().NotBeNull();
        _viewModel.SelectedAssetInstallation!.Path.Should().Be(zeroHourDir);
    }

    /// <summary>
    /// Tests that resolved asset previews are applied to matching canvas items.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task Previews_Apply_WhenServiceResolves()
    {
        // Arrange
        const string redPixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";
        var entries = new List<WndDrawDataEntry> { new("MenuButton", WndRgbaColor.White, WndRgbaColor.White) };
        while (entries.Count < WndConstants.DrawData.EntryCount)
        {
            entries.Add(WndDrawDataEntry.Empty);
        }

        var doc =
            "FILE_VERSION = 2;\n" +
            "WINDOW\n" +
            "  WINDOWTYPE = PUSHBUTTON;\n" +
            "  SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: 100 40, CREATIONRESOLUTION: 800 600;\n" +
            $"  ENABLEDDRAWDATA = {new WndDrawDataSet(entries)};\n" +
            "END\n";
        _mockImageAssetService
            .Setup(s => s.GetImagesAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<bool>()))
            .ReturnsAsync(OperationResult<IReadOnlyDictionary<string, byte[]>>.CreateSuccess(
                new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["MenuButton"] = Convert.FromBase64String(redPixelPng),
                }));
        var gameDir = Path.Combine(_tempDirectory, "Game");
        Directory.CreateDirectory(gameDir);
        var installation = new GameInstallation(gameDir, GameInstallationType.Steam)
        {
            HasZeroHour = true,
            ZeroHourPath = gameDir,
        };
        _mockGameInstallService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([installation]));

        // Act
        await _viewModel.LoadFromTextAsync(doc, null);

        // Assert
        _viewModel.CanvasItems.Should().ContainSingle();
        for (var attempt = 0; attempt < 200 && !_viewModel.CanvasItems[0].HasImage; attempt++)
        {
            Dispatcher.UIThread.RunJobs(null);
            await Task.Delay(20);
        }

        _viewModel.CanvasItems[0].HasImage.Should().BeTrue();
        _viewModel.CanvasItems[0].Image.Should().NotBeNull();
    }

    /// <summary>
    /// Tests that canvas items are offset by padding so every document stays pannable.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CanvasItems_OffsetByPadding()
    {
        // Act
        await _viewModel.LoadFromTextAsync(SampleDocument, null);

        // Assert
        var item = _viewModel.CanvasItems.First(i => i.Window.ControlType == WndControlType.PushButton);
        item.X.Should().Be((10 + WndConstants.Editor.CanvasPadding) * _viewModel.Zoom);
        item.Y.Should().Be((20 + WndConstants.Editor.CanvasPadding) * _viewModel.Zoom);
        _viewModel.CanvasWidth.Should().Be(800 + (WndConstants.Editor.CanvasPadding * 2));
        _viewModel.CanvasHeight.Should().Be(600 + (WndConstants.Editor.CanvasPadding * 2));
    }

    /// <summary>
    /// Tests that three-piece button art requests left, middle, and right images.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task Previews_RequestThreePieceNames()
    {
        // Arrange
        var entries = new List<WndDrawDataEntry>();
        for (var i = 0; i < WndConstants.DrawData.EntryCount; i++)
        {
            entries.Add(WndDrawDataEntry.Empty);
        }

        entries[0] = new WndDrawDataEntry("BtnLeft", WndRgbaColor.White, WndRgbaColor.White);
        entries[5] = new WndDrawDataEntry("BtnMiddle", WndRgbaColor.White, WndRgbaColor.White);
        entries[6] = new WndDrawDataEntry("BtnRight", WndRgbaColor.White, WndRgbaColor.White);
        var doc =
            "FILE_VERSION = 2;\n" +
            "WINDOW\n" +
            "  WINDOWTYPE = PUSHBUTTON;\n" +
            "  SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: 100 40, CREATIONRESOLUTION: 800 600;\n" +
            $"  ENABLEDDRAWDATA = {new WndDrawDataSet(entries)};\n" +
            "END\n";
        IReadOnlyCollection<string>? requested = null;
        _mockImageAssetService
            .Setup(s => s.GetImagesAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<bool>()))
            .Callback<IReadOnlyCollection<string>, string, string?, string?, CancellationToken, IReadOnlyCollection<string>?, bool>((names, _, _, _, _, _, _) => requested = names)
            .ReturnsAsync(OperationResult<IReadOnlyDictionary<string, byte[]>>.CreateSuccess(
                new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)));
        SetupSingleInstallation();

        // Act
        await _viewModel.LoadFromTextAsync(doc, null);
        for (var attempt = 0; attempt < 200 && requested == null; attempt++)
        {
            Dispatcher.UIThread.RunJobs(null);
            await Task.Delay(20);
        }

        // Assert
        requested.Should().NotBeNull();
        requested.Should().BeEquivalentTo("BtnLeft", "BtnMiddle", "BtnRight");
    }

    /// <summary>
    /// Tests that static text overlays its content without an image or fill.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task CanvasItems_StaticTextShowsContentOnly()
    {
        // Arrange
        var doc =
            "FILE_VERSION = 2;\n" +
            "WINDOW\n" +
            "  WINDOWTYPE = STATICTEXT;\n" +
            "  SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: 100 20, CREATIONRESOLUTION: 800 600;\n" +
            "  TEXT = \"Hello\";\n" +
            "END\n";

        // Act
        await _viewModel.LoadFromTextAsync(doc, null);

        // Assert
        _viewModel.CanvasItems.Should().ContainSingle();
        var item = _viewModel.CanvasItems[0];
        item.ContentText.Should().Be("Hello");
        item.HasImage.Should().BeFalse();
        item.HasFill.Should().BeFalse();
        item.ShowNameTag.Should().BeFalse();
    }

    /// <summary>
    /// Tests that generic windows without the IMAGE flag show their draw-data fill.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task CanvasItems_GenericWithoutImageFlagShowsFill()
    {
        // Arrange
        var entries = new List<WndDrawDataEntry>
        {
            new("Backdrop", new WndRgbaColor(10, 20, 30, 255), new WndRgbaColor(40, 50, 60, 255)),
        };
        while (entries.Count < WndConstants.DrawData.EntryCount)
        {
            entries.Add(WndDrawDataEntry.Empty);
        }

        var doc =
            "FILE_VERSION = 2;\n" +
            "WINDOW\n" +
            "  WINDOWTYPE = USER;\n" +
            "  SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: 100 40, CREATIONRESOLUTION: 800 600;\n" +
            "  STATUS = ENABLED;\n" +
            $"  ENABLEDDRAWDATA = {new WndDrawDataSet(entries)};\n" +
            "END\n";

        // Act
        await _viewModel.LoadFromTextAsync(doc, null);

        // Assert
        _viewModel.CanvasItems.Should().ContainSingle();
        var item = _viewModel.CanvasItems[0];
        item.HasImage.Should().BeFalse();
        item.HasFill.Should().BeTrue();
        item.HasBorderOverlay.Should().BeTrue();
    }

    /// <summary>
    /// Tests that the asset status reports resolved counts and missing names.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task AssetStatus_ReportsResolvedAndMissing()
    {
        // Arrange
        const string redPixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";
        _mockLocalizationService
            .Setup(s => s.GetString("Tools.WndEditor.Assets.ResolvedStatus", It.IsAny<object?[]>()))
            .Returns((string _, object?[] args) => $"{args[0]} of {args[1]} images");
        _mockLocalizationService
            .Setup(s => s.GetString("Tools.WndEditor.Assets.MissingTooltip", It.IsAny<object?[]>()))
            .Returns((string _, object?[] args) => $"Missing images: {args[0]}");
        var doc =
            "FILE_VERSION = 2;\n" +
            "WINDOW\n" +
            "  WINDOWTYPE = PUSHBUTTON;\n" +
            "  SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: 100 40, CREATIONRESOLUTION: 800 600;\n" +
            $"  ENABLEDDRAWDATA = {DrawDataWith("Found", 0)};\n" +
            "  CHILD\n" +
            "  WINDOW\n" +
            "    WINDOWTYPE = USER;\n" +
            "    SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: 50 20, CREATIONRESOLUTION: 800 600;\n" +
            "    STATUS = ENABLED+IMAGE;\n" +
            $"    ENABLEDDRAWDATA = {DrawDataWith("Lost", 0)};\n" +
            "  END\n" +
            "  ENDALLCHILDREN\n" +
            "END\n";
        _mockImageAssetService
            .Setup(s => s.GetImagesAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<bool>()))
            .ReturnsAsync(OperationResult<IReadOnlyDictionary<string, byte[]>>.CreateSuccess(
                new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Found"] = Convert.FromBase64String(redPixelPng),
                }));
        SetupSingleInstallation();

        // Act
        await _viewModel.LoadFromTextAsync(doc, null);
        for (var attempt = 0; attempt < 200 && !_viewModel.AssetStatusText.Contains("images", StringComparison.Ordinal); attempt++)
        {
            Dispatcher.UIThread.RunJobs(null);
            await Task.Delay(20);
        }

        // Assert
        _viewModel.AssetStatusText.Should().Be("1 of 2 images");
        _viewModel.AssetStatusTooltip.Should().Be("Missing images: Lost");
    }

    /// <summary>
    /// Tests that opening a file inside a Generals folder selects Generals assets over the Zero Hour default.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task AssetInstall_AutoSelectsInstallationContainingFile()
    {
        // Arrange
        var generalsDir = Path.Combine(_tempDirectory, "Generals");
        var zeroHourDir = Path.Combine(_tempDirectory, "ZeroHour");
        Directory.CreateDirectory(generalsDir);
        Directory.CreateDirectory(zeroHourDir);
        var installation = new GameInstallation(_tempDirectory, GameInstallationType.Steam)
        {
            HasGenerals = true,
            GeneralsPath = generalsDir,
            HasZeroHour = true,
            ZeroHourPath = zeroHourDir,
        };
        _mockGameInstallService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([installation]));

        // Act
        await _viewModel.LoadFromTextAsync(SampleDocument, Path.Combine(generalsDir, "Menu.wnd"));

        // Assert
        _viewModel.SelectedAssetInstallation.Should().NotBeNull();
        _viewModel.SelectedAssetInstallation!.Path.Should().Be(generalsDir);
    }

    /// <summary>
    /// Tests that string-table labels resolve to localized text while entry fields stay literal.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task ContentText_ResolvesStringTableLabels()
    {
        // Arrange
        var doc =
            "FILE_VERSION = 2;\n" +
            "WINDOW\n" +
            "  WINDOWTYPE = PUSHBUTTON;\n" +
            "  SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: 100 40, CREATIONRESOLUTION: 800 600;\n" +
            "  TEXT = \"GUI:Accept\";\n" +
            "  CHILD\n" +
            "  WINDOW\n" +
            "    WINDOWTYPE = ENTRYFIELD;\n" +
            "    SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: 50 20, CREATIONRESOLUTION: 800 600;\n" +
            "    TEXT = \"GUI:Accept\";\n" +
            "  END\n" +
            "  ENDALLCHILDREN\n" +
            "END\n";
        _mockStringTableService
            .Setup(s => s.GetStringsAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<bool>()))
            .ReturnsAsync(OperationResult<IReadOnlyDictionary<string, string>>.CreateSuccess(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["GUI:Accept"] = "&Accept",
                }));
        SetupSingleInstallation();

        // Act
        await _viewModel.LoadFromTextAsync(doc, null);
        for (var attempt = 0; attempt < 200 && _viewModel.CanvasItems.First(i => i.Window.ControlType == WndControlType.PushButton).ContentText != "Accept"; attempt++)
        {
            Dispatcher.UIThread.RunJobs(null);
            await Task.Delay(20);
        }

        // Assert
        _viewModel.CanvasItems.First(i => i.Window.ControlType == WndControlType.PushButton).ContentText.Should().Be("Accept");
        _viewModel.CanvasItems.First(i => i.Window.ControlType == WndControlType.EntryField).ContentText.Should().Be("GUI:Accept");
    }

    /// <summary>
    /// Tests that check box glyphs attach and shift the text right.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task CanvasItems_CheckBoxGlyph_ShiftsText()
    {
        // Arrange
        const string redPixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";
        var entries = new List<WndDrawDataEntry>();
        for (var i = 0; i < WndConstants.DrawData.EntryCount; i++)
        {
            entries.Add(WndDrawDataEntry.Empty);
        }

        entries[1] = new WndDrawDataEntry("BoxGlyph", WndRgbaColor.White, WndRgbaColor.White);
        var doc =
            "FILE_VERSION = 2;\n" +
            "WINDOW\n" +
            "  WINDOWTYPE = CHECKBOX;\n" +
            "  SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: 100 20, CREATIONRESOLUTION: 800 600;\n" +
            "  TEXT = \"Label\";\n" +
            $"  ENABLEDDRAWDATA = {new WndDrawDataSet(entries)};\n" +
            "END\n";
        _mockImageAssetService
            .Setup(s => s.GetImagesAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<bool>()))
            .ReturnsAsync(OperationResult<IReadOnlyDictionary<string, byte[]>>.CreateSuccess(
                new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["BoxGlyph"] = Convert.FromBase64String(redPixelPng),
                }));
        SetupSingleInstallation();

        // Act
        await _viewModel.LoadFromTextAsync(doc, null);
        for (var attempt = 0; attempt < 200 && !_viewModel.CanvasItems[0].HasGlyph; attempt++)
        {
            Dispatcher.UIThread.RunJobs(null);
            await Task.Delay(20);
        }

        // Assert
        var item = _viewModel.CanvasItems[0];
        item.HasGlyph.Should().BeTrue();
        item.ContentText.Should().Be("Label");
        item.ContentTextPadding.Left.Should().BeGreaterThan(4);
    }

    /// <summary>
    /// Tests that unnamed windows get muted fallback tags while named ones stay primary.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CanvasItems_NameFallback_FlagsUnnamedWindows()
    {
        // Arrange
        var doc =
            "FILE_VERSION = 2;\n" +
            "WINDOW\n" +
            "  WINDOWTYPE = STATICTEXT;\n" +
            "  SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: 100 20, CREATIONRESOLUTION: 800 600;\n" +
            "  CHILD\n" +
            "  WINDOW\n" +
            "    WINDOWTYPE = STATICTEXT;\n" +
            "    SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: 50 20, CREATIONRESOLUTION: 800 600;\n" +
            "    NAME = Menu.wnd:Named;\n" +
            "  END\n" +
            "  ENDALLCHILDREN\n" +
            "END\n";

        // Act
        await _viewModel.LoadFromTextAsync(doc, null);

        // Assert
        _viewModel.CanvasItems.Should().HaveCount(2);
        _viewModel.CanvasItems[0].IsFallbackLabel.Should().BeTrue();
        _viewModel.CanvasItems[0].ShowFallbackNameTag.Should().BeTrue();
        _viewModel.CanvasItems[0].ShowPrimaryNameTag.Should().BeFalse();
        _viewModel.CanvasItems[1].IsFallbackLabel.Should().BeFalse();
        _viewModel.CanvasItems[1].ShowPrimaryNameTag.Should().BeTrue();
        _viewModel.CanvasItems[1].ShowFallbackNameTag.Should().BeFalse();
    }

    /// <summary>
    /// Tests that list box scrollbar overlays attach at the right edge.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task CanvasItems_ListboxScrollbar_AttachesOverlays()
    {
        // Arrange
        const string redPixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";
        var up = DrawDataWith("ScrollUp", 0);
        var down = DrawDataWith("ScrollDown", 0);
        var thumb = DrawDataWith("ScrollThumb", 0);
        var doc =
            "FILE_VERSION = 2;\n" +
            "WINDOW\n" +
            "  WINDOWTYPE = SCROLLLISTBOX;\n" +
            "  SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: 100 60, CREATIONRESOLUTION: 800 600;\n" +
            $"  LISTBOXENABLEDUPBUTTONDRAWDATA = {up};\n" +
            $"  LISTBOXENABLEDDOWNBUTTONDRAWDATA = {down};\n" +
            $"  LISTBOXENABLEDSLIDERDRAWDATA = {thumb};\n" +
            "END\n";
        _mockImageAssetService
            .Setup(s => s.GetImagesAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<bool>()))
            .ReturnsAsync(OperationResult<IReadOnlyDictionary<string, byte[]>>.CreateSuccess(
                new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ScrollUp"] = Convert.FromBase64String(redPixelPng),
                    ["ScrollDown"] = Convert.FromBase64String(redPixelPng),
                    ["ScrollThumb"] = Convert.FromBase64String(redPixelPng),
                }));
        SetupSingleInstallation();

        // Act
        await _viewModel.LoadFromTextAsync(doc, null);
        for (var attempt = 0; attempt < 200 && _viewModel.CanvasItems[0].Overlays.Count != 3; attempt++)
        {
            Dispatcher.UIThread.RunJobs(null);
            await Task.Delay(20);
        }

        // Assert
        var item = _viewModel.CanvasItems[0];
        item.Overlays.Should().HaveCount(3);
        item.Overlays[0].Y.Should().Be(0);
        item.Overlays[1].Y.Should().Be(item.Height - item.Overlays[1].Height);
        item.Overlays[2].Y.Should().Be(item.Overlays[0].Height);
        item.Overlays[2].Height.Should().Be(item.Height - item.Overlays[0].Height - item.Overlays[1].Height);
        item.Overlays.Should().OnlyContain(overlay => overlay.X + overlay.Width == item.Width);
    }

    /// <summary>
    /// Tests that refreshing files loads the directory tree recursively, skips empty directories, and strips extensions.
    /// </summary>
    [Fact]
    public void RefreshFiles_LoadsTreeRecursively_SkipsEmptyDirectories_StripsExtension()
    {
        // Arrange
        var rootDir = Path.Combine(_tempDirectory, "GameFilesEdited");
        var gen1080 = Path.Combine(rootDir, "Gen1080", "Window", "Menus");
        var emptyDir = Path.Combine(rootDir, "EmptyFolder", "SubEmpty");
        var nonWndDir = Path.Combine(rootDir, "NonWndFolder");

        Directory.CreateDirectory(gen1080);
        Directory.CreateDirectory(emptyDir);
        Directory.CreateDirectory(nonWndDir);

        File.WriteAllText(Path.Combine(gen1080, "Defeat.wnd"), "FILE_VERSION = 2;");
        File.WriteAllText(Path.Combine(gen1080, "DisconnectScreen.wnd"), "FILE_VERSION = 2;");
        File.WriteAllText(Path.Combine(rootDir, "Gen1080", "Window", "ControlBar.wnd"), "FILE_VERSION = 2;");
        File.WriteAllText(Path.Combine(nonWndDir, "readme.txt"), "not a wnd file");

        // Act
        _viewModel.FilesDirectory = rootDir;

        // Assert
        _viewModel.FilesDirectoryName.Should().Be("GameFilesEdited");
        _viewModel.Files.Should().HaveCount(1);

        var rootNode = _viewModel.Files[0];
        rootNode.Name.Should().Be("GameFilesEdited");
        rootNode.IsDirectory.Should().BeTrue();

        // Gen1080 should be present; EmptyFolder and NonWndFolder should be skipped!
        rootNode.Children.Should().HaveCount(1);
        var gen1080Node = rootNode.Children[0];
        gen1080Node.Name.Should().Be("Gen1080");

        var windowNode = gen1080Node.Children[0];
        windowNode.Name.Should().Be("Window");

        // Window contains Menus (folder) and ControlBar (file without .wnd)
        windowNode.Children.Should().HaveCount(2);

        var menusNode = windowNode.Children.First(c => c.IsDirectory);
        menusNode.Name.Should().Be("Menus");
        menusNode.Children.Select(c => c.Name).Should().BeEquivalentTo(["Defeat", "DisconnectScreen"]);
        menusNode.Children.All(c => c.IsFile).Should().BeTrue();

        var controlBarNode = windowNode.Children.First(c => c.IsFile);
        controlBarNode.Name.Should().Be("ControlBar");
    }

    /// <summary>
    /// Tests that opening a file inside the current directory retains the tree hierarchy and updates the active file.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SyncFilesDirectory_WhenFileInsideCurrentDirectory_RetainsTreeAndUpdatesIsCurrent()
    {
        // Arrange
        var rootDir = Path.Combine(_tempDirectory, "GameFilesEdited");
        var menusDir = Path.Combine(rootDir, "Window", "Menus");
        Directory.CreateDirectory(menusDir);

        var defeatPath = Path.Combine(menusDir, "Defeat.wnd");
        File.WriteAllText(defeatPath, "FILE_VERSION = 2;\nWINDOW\n  SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: 10 10, CREATIONRESOLUTION: 800 600;\nEND\n");

        _viewModel.FilesDirectory = rootDir;
        _viewModel.Files.Should().HaveCount(1);

        // Act
        await _viewModel.OpenFileAsync(defeatPath);

        // Assert - FilesDirectory stays as rootDir
        _viewModel.FilesDirectory.Should().Be(rootDir);

        // Defeat node should now be marked IsCurrent
        var defeatNode = _viewModel.Files[0].Children[0].Children[0].Children[0];
        defeatNode.Name.Should().Be("Defeat");
        defeatNode.IsCurrent.Should().BeTrue();
    }

    /// <summary>
    /// Tests that OpenFolderAsync populates the tree, switches to the Files tab, and opens the first WND file.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task OpenFolderAsync_WithValidDirectoryAndWndFiles_PopulatesFilesSwitchesTabAndOpensFirstFile()
    {
        // Arrange
        var rootDir = Path.Combine(_tempDirectory, "ProjectFolder");
        var subDir = Path.Combine(rootDir, "Sub", "Window");
        Directory.CreateDirectory(subDir);

        var firstWnd = Path.Combine(subDir, "Alpha.wnd");
        var secondWnd = Path.Combine(subDir, "Beta.wnd");
        File.WriteAllText(firstWnd, "FILE_VERSION = 2;\nWINDOW\n  SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: 10 10, CREATIONRESOLUTION: 800 600;\nEND\n");
        File.WriteAllText(secondWnd, "FILE_VERSION = 2;\nWINDOW\n  SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: 20 20, CREATIONRESOLUTION: 800 600;\nEND\n");

        // Act
        var result = await _viewModel.OpenFolderAsync(rootDir);

        // Assert
        result.Should().BeTrue();
        _viewModel.FilesDirectory.Should().Be(rootDir);
        _viewModel.LeftSidebarTabIndex.Should().Be(1);
        _viewModel.HasDocument.Should().BeTrue();
        _viewModel.FilePath.Should().Be(firstWnd);

        var alphaNode = _viewModel.Files[0].Children[0].Children[0].Children.First(c => c.Name == "Alpha");
        alphaNode.IsCurrent.Should().BeTrue();
    }

    /// <summary>
    /// Tests that OpenFolderAsync returns false when the specified folder does not exist.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task OpenFolderAsync_WithNonExistentDirectory_ReturnsFalse()
    {
        // Act
        var result = await _viewModel.OpenFolderAsync(Path.Combine(_tempDirectory, "DoesNotExist"));

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// Tests that OpenFolderAsync shows info when no WND files are found in the directory.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task OpenFolderAsync_DirectoryWithNoWndFiles_ShowsInfoNotification()
    {
        // Arrange
        var emptyDir = Path.Combine(_tempDirectory, "NoWndHere");
        Directory.CreateDirectory(emptyDir);

        // Act
        var result = await _viewModel.OpenFolderAsync(emptyDir);

        // Assert
        result.Should().BeTrue();
        _viewModel.FilesDirectory.Should().Be(emptyDir);
        _viewModel.LeftSidebarTabIndex.Should().Be(1);
        _mockNotificationService.Verify(
            n => n.ShowInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that ParseControlBarSchemeIni parses retail space-separated INI format,
    /// extracts ImageName from multi-line ImagePart blocks, and prefers America scheme.
    /// </summary>
    [Fact]
    public void ParseControlBarSchemeIni_RetailSchemeExcerpt_ParsesCorrectOverrides()
    {
        // Arrange
        const string retailIni =
            "; Retail Zero Hour ControlBarScheme.ini excerpt\n" +
            "ControlBarScheme ControlBarSchemeAmerica\n" +
            "  ScreenHeight 768\n" +
            "  Side America\n" +
            "  QueueButtonImage SCBigButton\n" +
            "  RightHUDImage SALogo\n" +
            "  OptionsButtonEnable SAOptions\n" +
            "  IdleWorkerButtonEnable SAWorker\n" +
            "  BuddyButtonEnable SAChat\n" +
            "  BeaconButtonEnable SABeacon\n" +
            "  GeneralButtonEnable SAGeneral\n" +
            "  UAttackButtonEnable SAUAttackI\n" +
            "  ExpBarForegroundImage SAExpBar\n" +
            "  ImagePart\n" +
            "    Position X:0 Y:0\n" +
            "    Size X:1024 Y:192\n" +
            "    ImageName InGameUIAmericaBase\n" +
            "  End\n" +
            "End\n" +
            "\n" +
            "ControlBarScheme ControlBarSchemeChina\n" +
            "  ScreenHeight 768\n" +
            "  Side China\n" +
            "  QueueButtonImage SCBigButton\n" +
            "  RightHUDImage SNLogo\n" +
            "  OptionsButtonEnable SNOptions\n" +
            "  ImagePart\n" +
            "    Position X:0 Y:0\n" +
            "    Size X:1024 Y:192\n" +
            "    ImageName InGameUIChinaBase\n" +
            "  End\n" +
            "End\n";

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Act
        WndEditorViewModel.ParseControlBarSchemeIni(retailIni, dict);

        // Assert
        dict["BackgroundMarker"].Should().Be("InGameUIAmericaBase");
        dict["RightHUD"].Should().Be("SALogo");
        dict["ButtonOptions"].Should().Be("SAOptions");
        dict["ButtonIdleWorker"].Should().Be("SAWorker");
        dict["ButtonChat"].Should().Be("SAChat");
        dict["ButtonPlaceBeacon"].Should().Be("SABeacon");
        dict["ButtonGeneral"].Should().Be("SAGeneral");
        dict["ButtonUAttack"].Should().Be("SAUAttackI");
        dict["ExpBarForeground"].Should().Be("SAExpBar");
        dict["QueueButtonImage"].Should().Be("SCBigButton");
    }

    /// <summary>
    /// Tests that ParseControlBarSchemeIni falls back to the first scheme if America is absent.
    /// </summary>
    [Fact]
    public void ParseControlBarSchemeIni_CustomSingleScheme_FallsBackToFirstScheme()
    {
        // Arrange
        const string ini =
            "ControlBarScheme CustomFactionScheme\n" +
            "  RightHUDImage CustomLogo\n" +
            "  OptionsButtonEnable CustomOptions\n" +
            "  ImagePart\n" +
            "    ImageName CustomHUDImage\n" +
            "  End\n" +
            "End\n";

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Act
        WndEditorViewModel.ParseControlBarSchemeIni(ini, dict);

        // Assert
        dict["RightHUD"].Should().Be("CustomLogo");
        dict["ButtonOptions"].Should().Be("CustomOptions");
        dict["BackgroundMarker"].Should().Be("CustomHUDImage");
    }

    /// <summary>
    /// Tests that single-token scheme headers are correctly extracted and matched.
    /// </summary>
    [Fact]
    public void ParseControlBarSchemeIni_SingleTokenHeader_SelectsCorrectScheme()
    {
        // Arrange
        const string ini =
            "ControlBarSchemeChina\n" +
            "  RightHUDImage ChinaLogo\n" +
            "End\n" +
            "ControlBarSchemeAmerica\n" +
            "  RightHUDImage AmericaLogo\n" +
            "End\n";

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Act
        WndEditorViewModel.ParseControlBarSchemeIni(ini, dict, "ControlBarSchemeAmerica");

        // Assert
        dict[WndConstants.ControlBarScheme.RightHUDKey].Should().Be("AmericaLogo");
    }

    /// <summary>
    /// Tests that keys with prefixes matching keywords are not treated as block headers.
    /// </summary>
    [Fact]
    public void ParseControlBarSchemeIni_KeyStartsWithSchemeOrImagePartPrefix_ParsedAsKeyValue()
    {
        // Arrange
        const string ini =
            "ControlBarSchemes = 5\n" +
            "ImagePartsTotal = 10\n" +
            "RightHUDImage = AmericaLogo\n";

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Act
        WndEditorViewModel.ParseControlBarSchemeIni(ini, dict);

        // Assert
        dict[WndConstants.ControlBarScheme.RightHUDKey].Should().Be("AmericaLogo");
        dict.Should().NotContainKey("ControlBarSchemes");
        dict.Should().NotContainKey("ImagePartsTotal");
    }

    /// <summary>
    /// Tests that an inline ImagePart statement inside an open block does not reset the latch prematurely.
    /// </summary>
    [Fact]
    public void ParseControlBarSchemeIni_RepeatedOrInlineImagePartInBlock_DoesNotResetLatchEarly()
    {
        // Arrange
        const string ini =
            "ControlBarScheme AmericaScheme\n" +
            "  ImagePart\n" +
            "    Position X:0 Y:0\n" +
            "    ImagePart ImageName = InnerImage\n" +
            "  End\n" +
            "  RightHUDImage AfterEndLogo\n" +
            "End\n";

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Act
        WndEditorViewModel.ParseControlBarSchemeIni(ini, dict);

        // Assert
        dict[WndConstants.ControlBarScheme.BackgroundMarkerKey].Should().Be("InnerImage");
        dict[WndConstants.ControlBarScheme.RightHUDKey].Should().Be("AfterEndLogo");
    }

    /// <summary>
    /// Tests that comment stripping does not truncate URLs or quoted comments.
    /// </summary>
    [Fact]
    public void ParseControlBarSchemeIni_CommentStripping_PreservesUrlAndQuotedComments()
    {
        // Arrange
        const string ini =
            "RightHUDImage = \"SALogo;NotComment // StillNotComment\"\n" +
            "OptionsButtonEnable = http://myserver.com/path // RealComment\n";

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Act
        WndEditorViewModel.ParseControlBarSchemeIni(ini, dict);

        // Assert
        dict[WndConstants.ControlBarScheme.RightHUDKey].Should().Be("SALogo;NotComment // StillNotComment");
        dict[WndConstants.ControlBarScheme.ButtonOptionsKey].Should().Be("http://myserver.com/path");
    }

    /// <summary>
    /// Tests that inline ImagePart does not latch block mode, allowing subsequent flat keys to be parsed.
    /// </summary>
    [Fact]
    public void ParseControlBarSchemeIni_InlineImagePart_DoesNotSwallowSubsequentLines()
    {
        // Arrange
        const string ini =
            "ImagePart ImageName = CustomHUDImage\n" +
            "RightHUDImage = CustomLogo\n" +
            "OptionsButtonEnable: CustomOptions\n";

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Act
        WndEditorViewModel.ParseControlBarSchemeIni(ini, dict);

        // Assert
        dict[WndConstants.ControlBarScheme.BackgroundMarkerKey].Should().Be("CustomHUDImage");
        dict[WndConstants.ControlBarScheme.RightHUDKey].Should().Be("CustomLogo");
        dict[WndConstants.ControlBarScheme.ButtonOptionsKey].Should().Be("CustomOptions");
    }

    /// <summary>
    /// Tests that ParseControlBarSchemeIni parses flat format with equals signs and inline comments.
    /// </summary>
    [Fact]
    public void ParseControlBarSchemeIni_FlatFormatWithComments_ParsesCorrectly()
    {
        // Arrange
        const string ini =
            "; Top-level comment\n" +
            "RightHUDImage = CustomLogo ; inline comment\n" +
            "OptionsButtonEnable = CustomOptions // c++ style comment\n" +
            "ImagePart ImageName: CustomHUDImage\n";

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Act
        WndEditorViewModel.ParseControlBarSchemeIni(ini, dict);

        // Assert
        dict["RightHUD"].Should().Be("CustomLogo");
        dict["ButtonOptions"].Should().Be("CustomOptions");
        dict["BackgroundMarker"].Should().Be("CustomHUDImage");
    }

    private static string DrawDataWith(string name, int index)
    {
        var entries = new List<WndDrawDataEntry>();
        for (var i = 0; i < WndConstants.DrawData.EntryCount; i++)
        {
            entries.Add(WndDrawDataEntry.Empty);
        }

        entries[index] = new WndDrawDataEntry(name, WndRgbaColor.White, WndRgbaColor.White);
        return new WndDrawDataSet(entries).ToString();
    }

    private void SetupSingleInstallation()
    {
        var gameDir = Path.Combine(_tempDirectory, "Game");
        Directory.CreateDirectory(gameDir);
        var installation = new GameInstallation(gameDir, GameInstallationType.Steam)
        {
            HasZeroHour = true,
            ZeroHourPath = gameDir,
        };
        _mockGameInstallService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([installation]));
    }
}
