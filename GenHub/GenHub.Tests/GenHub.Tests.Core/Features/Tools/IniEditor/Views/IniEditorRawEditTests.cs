using Avalonia.Headless.XUnit;
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
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using static GenHub.Tests.Core.Features.Tools.IniEditor.IniEditorTestFactory;

namespace GenHub.Tests.Core.Features.Tools.IniEditor.Views;

/// <summary>
/// Unit tests for raw-preview edit rejection in <see cref="IniEditorViewModel"/>.
/// </summary>
public sealed class IniEditorRawEditTests
{
    /// <summary>
    /// Verifies that pasting multiple blocks leaves the target block untouched with a warning.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task TwoBlockPaste_LeavesTargetBlockUntouchedAsync()
    {
        var warnings = new List<string>();
        var mockNotifications = NotificationsCapturing(warnings);
        using var viewModel = CreateViewModel(mockNotifications.Object);
        var block = await SetupBlockAsync(viewModel, "Tank");
        warnings.Clear();

        viewModel.RawPreviewText = "Object Tank\n  Health = 5\nEnd\nObject Other\n  Health = 6\nEnd\n";

        Assert.True(await WaitForAsync(() => warnings.Count > 0, TimeSpan.FromSeconds(5)));
        Assert.Equal("Tools.IniEditor.Preview.RawEditMultiBlockMessage", warnings[0]);
        Assert.Equal("Object", block.BlockType);
        Assert.Equal("Tank", block.Name);
        Assert.Equal("100", block.Fields.Single(field => field.Key == "Health").Value);
    }

    /// <summary>
    /// Verifies that pasting a block with file-level entries leaves the target block untouched.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task SingleBlockWithGlobalFields_LeavesTargetBlockUntouchedAsync()
    {
        var warnings = new List<string>();
        var mockNotifications = NotificationsCapturing(warnings);
        using var viewModel = CreateViewModel(mockNotifications.Object);
        var block = await SetupBlockAsync(viewModel, "Tank");
        warnings.Clear();

        viewModel.RawPreviewText = "SomeGlobal = 1\nObject Tank\n  Health = 5\nEnd\n";

        Assert.True(await WaitForAsync(() => warnings.Count > 0, TimeSpan.FromSeconds(5)));
        Assert.Equal("Tools.IniEditor.Preview.RawEditFileScopeMessage", warnings[0]);
        Assert.Equal("Object", block.BlockType);
        Assert.Equal("Tank", block.Name);
        Assert.Equal("100", block.Fields.Single(field => field.Key == "Health").Value);
    }

    private static async Task<IniBlock> SetupBlockAsync(IniEditorViewModel viewModel, string blockName)
    {
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Object";
        viewModel.NewBlockName = blockName;
        viewModel.AddBlockCommand.Execute(null);

        var node = viewModel.VisibleRootNodes
            .SelectMany(FlattenNodes)
            .First(n => string.Equals(n.Block?.Name, blockName, StringComparison.Ordinal));
        viewModel.SelectedNode = node;
        Assert.NotNull(viewModel.SelectedNode);

        var block = viewModel.SelectedNode.Block!;
        block.Fields.RemoveAll(field => string.Equals(field.Key, "Health", StringComparison.OrdinalIgnoreCase));

        viewModel.NewFieldKey = "Health";
        viewModel.NewFieldValue = "100";
        viewModel.AddFieldCommand.Execute(null);

        return block;
    }

    private static IEnumerable<IniTreeNodeViewModel> FlattenNodes(IniTreeNodeViewModel node)
    {
        yield return node;
        foreach (var child in node.Children.SelectMany(FlattenNodes))
        {
            yield return child;
        }
    }

    private static Mock<INotificationService> NotificationsCapturing(List<string> warnings)
    {
        var mockNotifications = new Mock<INotificationService>();
        mockNotifications
            .Setup(notifications => notifications.ShowWarning(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<bool>()))
            .Callback<string, string, int?, bool>((_, message, _, _) => warnings.Add(message));
        return mockNotifications;
    }
}
