using Avalonia.Headless.XUnit;
using FluentAssertions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Models.Tools.RmlEditor;
using GenHub.Features.Tools.RmlEditor.Services;
using GenHub.Features.Tools.RmlEditor.ViewModels;
using GenHub.Features.Tools.TextureEditor.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.Tools.RmlEditor.ViewModels;

/// <summary>
/// Unit tests for <see cref="RmlEditorViewModel"/>.
/// </summary>
public sealed class RmlEditorViewModelTests : IDisposable
{
    private const string SampleDocument =
        "<rml>\n" +
        "  <head>\n" +
        "    <title>Menu</title>\n" +
        "  </head>\n" +
        "  <body>\n" +
        "    <div id=\"panel\">\n" +
        "      <button id=\"start\">Start</button>\n" +
        "    </div>\n" +
        "  </body>\n" +
        "</rml>\n";

    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<ILocalizationService> _localization = new();
    private readonly Mock<IDialogService> _dialogs = new();
    private readonly RmlEditorViewModel _viewModel;
    private readonly string _tempDirectory;
    private readonly string _samplePath;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RmlEditorViewModelTests"/> class.
    /// </summary>
    public RmlEditorViewModelTests()
    {
        _localization.Setup(s => s.GetString(It.IsAny<string>(), It.IsAny<object?[]>()))
            .Returns((string key, object?[] args) => args is { Length: > 0 } ? $"{key} {string.Join(' ', args)}" : key);
        _dialogs.Setup(s => s.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(true);
        var documents = new RmlDocumentService(new Mock<ILogger<RmlDocumentService>>().Object);
        var styles = new RcssDocumentService(new Mock<ILogger<RcssDocumentService>>().Object);
        var builder = new RmlPreviewBuilder(styles, _localization.Object);
        var bitmaps = new TextureBitmapService(new Mock<ISageTextureCodec>().Object, new Mock<ILogger<TextureBitmapService>>().Object);
        var images = new RmlImageResolver(bitmaps, new Mock<ILogger<RmlImageResolver>>().Object);
        _viewModel = new RmlEditorViewModel(
            documents,
            styles,
            builder,
            images,
            _notifications.Object,
            _localization.Object,
            _dialogs.Object,
            new Mock<ILogger<RmlEditorViewModel>>().Object);
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _samplePath = Path.Combine(_tempDirectory, "Menu.rml");
        File.WriteAllText(_samplePath, SampleDocument);
    }

    /// <summary>
    /// Tests that opening a file builds the element tree.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task OpenFileAsync_BuildsTreeAsync()
    {
        var opened = await _viewModel.OpenFileAsync(_samplePath);

        opened.Should().BeTrue();
        _viewModel.HasDocument.Should().BeTrue();
        _viewModel.RootNodes.Should().HaveCount(2);
        _viewModel.RootNodes.SelectMany(r => r.Children).Should().Contain(n => n.DisplayName.Contains("panel"));
    }

    /// <summary>
    /// Tests attribute editing with undo and redo.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task CommitAttribute_UndoRedo_RestoresValueAsync()
    {
        await _viewModel.OpenFileAsync(_samplePath);
        var panel = _viewModel.RootNodes.SelectMany(r => r.Children).First(n => n.DisplayName.Contains("panel"));

        _viewModel.CommitAttribute(panel.Node.Id, "class", "active");
        ((RmlElement)panel.Node).GetAttribute("class").Should().Be("active");

        _viewModel.UndoCommand.Execute(null);
        ((RmlElement)panel.Node).GetAttribute("class").Should().BeNull();

        _viewModel.RedoCommand.Execute(null);
        ((RmlElement)panel.Node).GetAttribute("class").Should().Be("active");
    }

    /// <summary>
    /// Tests that deleting the selected node removes it with undo support.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task DeleteCommand_RemovesSelectedNodeAsync()
    {
        await _viewModel.OpenFileAsync(_samplePath);
        var button = FindNode("start");
        _viewModel.SelectNode(button.Node.Id);

        _viewModel.DeleteCommand.Execute(null);

        FindNodeOrNull("start").Should().BeNull();
        _viewModel.UndoCommand.Execute(null);
        FindNode("start").Should().NotBeNull();
    }

    /// <summary>
    /// Tests style rule add and delete.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task AddAndDeleteRule_UpdatesStyleRulesAsync()
    {
        var sheetPath = Path.Combine(_tempDirectory, "common.rcss");
        await File.WriteAllTextAsync(sheetPath, ".screen { color: white; }\n");
        await _viewModel.OpenFileAsync(_samplePath);

        _viewModel.AddRule(".panel");

        _viewModel.StyleRules.Should().Contain(r => r.Selectors == ".panel");
        _viewModel.SelectedStyleRule.Should().NotBeNull();
        _viewModel.DeleteStyleRuleCommand.Execute(null);
        _viewModel.StyleRules.Should().NotContain(r => r.Selectors == ".panel");
    }

    /// <summary>
    /// Tests that save writes the edited document back to disk.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task SaveCommand_WritesEditedDocumentAsync()
    {
        await _viewModel.OpenFileAsync(_samplePath);
        var panel = FindNode("panel");
        _viewModel.CommitAttribute(panel.Node.Id, "class", "active");

        await _viewModel.SaveCommand.ExecuteAsync(null);

        var saved = await File.ReadAllTextAsync(_samplePath);
        saved.Should().Contain("active");
        _viewModel.IsModified.Should().BeFalse();
    }

    /// <summary>
    /// Tests that applying source enables undo.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task ApplySourceCommand_EnablesUndoAsync()
    {
        await _viewModel.OpenFileAsync(_samplePath);
        _viewModel.CanUndo.Should().BeFalse();
        _viewModel.IsSourceView = true;
        _viewModel.SourceText = _viewModel.SourceText.Replace("Menu", "Edited");

        _viewModel.ApplySourceCommand.Execute(null);

        _viewModel.CanUndo.Should().BeTrue();
        _viewModel.UndoCommand.Execute(null);
        _viewModel.CanUndo.Should().BeFalse();
    }

    /// <summary>
    /// Tests selector commits with undo and selection gating.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task CommitRuleSelectors_UpdatesRuleWithUndoAsync()
    {
        var sheetPath = Path.Combine(_tempDirectory, "common.rcss");
        await File.WriteAllTextAsync(sheetPath, ".screen { color: white; }\n");
        await _viewModel.OpenFileAsync(_samplePath);
        var rule = _viewModel.StyleRules.First();
        _viewModel.SelectedStyleRule = rule;
        _viewModel.HasSelectedStyleRule.Should().BeTrue();
        _viewModel.CanEditSelectedRule.Should().BeTrue();

        _viewModel.CommitRuleSelectors(rule.SheetIndex, rule.RuleIndex, ".screen, .dark");

        _viewModel.StyleRules.Should().Contain(r => r.Selectors == ".screen, .dark");
        _viewModel.UndoCommand.Execute(null);
        _viewModel.StyleRules.Should().Contain(r => r.Selectors == ".screen");
    }

    /// <summary>
    /// Tests that cut followed by undo followed by paste inserts a distinct copy.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task CutUndoPaste_InsertsDistinctCopyAsync()
    {
        await _viewModel.OpenFileAsync(_samplePath);
        _viewModel.SelectNode(FindNode("start").Node.Id);
        _viewModel.CutCommand.Execute(null);
        _viewModel.UndoCommand.Execute(null);
        _viewModel.SelectNode(FindNode("panel").Node.Id);

        await _viewModel.PasteCommand.ExecuteAsync(null);

        var matches = _viewModel.RootNodes
            .SelectMany(r => new[] { r }.Concat(r.Children.SelectMany(Flatten)))
            .Select(n => n.Node)
            .OfType<RmlElement>()
            .Where(e => e.ElementId == "start")
            .ToList();
        matches.Should().HaveCount(2);
        matches.Distinct().Should().HaveCount(2);
    }

    /// <summary>
    /// Tests that style sheet links escaping the document folder are ignored.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task OpenFileAsync_StylesheetTraversal_BlockedAsync()
    {
        var outside = Path.Combine(_tempDirectory, "outside.rcss");
        await File.WriteAllTextAsync(outside, ".evil { color: red; }\n");
        var screenDirectory = Path.Combine(_tempDirectory, "screens");
        Directory.CreateDirectory(screenDirectory);
        var screenPath = Path.Combine(screenDirectory, "Menu.rml");
        var content = "<rml>\n" +
            "  <head>\n" +
            "    <link type=\"text/rcss\" href=\"../outside.rcss\" />\n" +
            $"    <link type=\"text/rcss\" href=\"{outside}\" />\n" +
            "  </head>\n" +
            "  <body><div /></body>\n" +
            "</rml>\n";
        await File.WriteAllTextAsync(screenPath, content);

        await _viewModel.OpenFileAsync(screenPath);

        _viewModel.StyleRules.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that validation reports success for the sample document.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task ValidateCommand_ReportsSuccessAsync()
    {
        await _viewModel.OpenFileAsync(_samplePath);

        _viewModel.ValidateDocumentCommand.Execute(null);

        _notifications.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.AtLeastOnce);
    }

    /// <summary>
    /// Releases test resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _viewModel.Dispose();
        Directory.Delete(_tempDirectory, recursive: true);
    }

    private static IEnumerable<RmlTreeNodeViewModel> Flatten(RmlTreeNodeViewModel node)
    {
        yield return node;
        foreach (var child in node.Children.SelectMany(Flatten))
        {
            yield return child;
        }
    }

    private RmlTreeNodeViewModel FindNode(string id)
    {
        return FindNodeOrNull(id) ?? throw new InvalidOperationException($"Node {id} not found.");
    }

    private RmlTreeNodeViewModel? FindNodeOrNull(string id)
    {
        return _viewModel.RootNodes
            .SelectMany(r => new[] { r }.Concat(r.Children.SelectMany(Flatten)))
            .FirstOrDefault(n => n.Node is RmlElement e && e.ElementId == id);
    }
}
