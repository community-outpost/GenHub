using Avalonia.Headless.XUnit;
using GenHub.Core.Constants;
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
using static GenHub.Tests.Core.Features.Tools.IniEditor.IniEditorTestFactory;

namespace GenHub.Tests.Core.Features.Tools.IniEditor.Views;

/// <summary>
/// Tests for block explorer grouping, compact rows, structured pair fields, canvas icons, and linked assets.
/// </summary>
public class IniEditorDensityTests
{
    /// <summary>
    /// Verifies that large documents group blocks by type with counts and reveal the active group.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task LargeDocument_GroupsBlocksByTypeAsync()
    {
        using var viewModel = CreateViewModel();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        for (var i = 0; i < 14; i++)
        {
            viewModel.NewBlockType = "Object";
            viewModel.NewBlockName = $"Obj{i}";
            viewModel.AddBlockCommand.Execute(null);
        }

        viewModel.NewBlockType = "Upgrade";
        viewModel.NewBlockName = "Up0";
        viewModel.AddBlockCommand.Execute(null);
        viewModel.NewBlockName = "Up1";
        viewModel.AddBlockCommand.Execute(null);

        Assert.Equal(16, viewModel.RootNodes.Count);
        Assert.Equal(2, viewModel.VisibleRootNodes.Count);
        Assert.All(viewModel.VisibleRootNodes, node => Assert.True(node.IsGroupHeader));
        Assert.Contains(viewModel.VisibleRootNodes, node => node.ShortName == "Object" && node.GroupCount == 14);
        Assert.Contains(viewModel.VisibleRootNodes, node => node.ShortName == "Upgrade" && node.GroupCount == 2);
        var upgradeHeader = viewModel.VisibleRootNodes.First(node => node.ShortName == "Upgrade");
        Assert.True(upgradeHeader.IsExpanded);
        Assert.Equal("Tools.IniEditor.Tree.CountGrouped", viewModel.BlockCountText);
    }

    /// <summary>
    /// Verifies that small documents stay flat and report the flat count text.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task SmallDocument_StaysFlatAsync()
    {
        using var viewModel = CreateViewModel();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Object";
        viewModel.NewBlockName = "Solo";
        viewModel.AddBlockCommand.Execute(null);

        Assert.Single(viewModel.VisibleRootNodes);
        Assert.False(viewModel.VisibleRootNodes[0].IsGroupHeader);
        Assert.Equal("Tools.IniEditor.Tree.CountFlat", viewModel.BlockCountText);
    }

    /// <summary>
    /// Verifies that disabling grouping flattens large documents.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task GroupingDisabled_FlattensLargeDocumentAsync()
    {
        using var viewModel = CreateViewModel();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        for (var i = 0; i < 14; i++)
        {
            viewModel.NewBlockType = "Object";
            viewModel.NewBlockName = $"Obj{i}";
            viewModel.AddBlockCommand.Execute(null);
        }

        Assert.Single(viewModel.VisibleRootNodes);

        viewModel.IsGroupByTypeEnabled = false;

        Assert.Equal(14, viewModel.VisibleRootNodes.Count);
        Assert.All(viewModel.VisibleRootNodes, node => Assert.False(node.IsGroupHeader));
    }

    /// <summary>
    /// Verifies that group headers show short names without the redundant type prefix.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task BlockRow_UsesShortNameWithoutTypePrefixAsync()
    {
        using var viewModel = CreateViewModel();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Object";
        viewModel.NewBlockName = "Boss_InfantryHacker";
        viewModel.AddBlockCommand.Execute(null);

        var node = viewModel.RootNodes[0];
        Assert.Equal("Boss_InfantryHacker", node.ShortName);
        Assert.Equal("Object Boss_InfantryHacker", node.DisplayName);
        Assert.Equal("Object", node.Badge);
    }

    /// <summary>
    /// Verifies that selecting a group header expands it and restores the previous editable
    /// selection so the properties panel stays populated instead of going empty.
    /// Group headers are navigation-only and can never hold the selection.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task GroupHeaderSelection_RestoresEditableSelectionAndPreservesFieldsAsync()
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
        var fieldCount = viewModel.FieldRows.Count;
        Assert.True(fieldCount > 0);

        var header = viewModel.VisibleRootNodes[0];
        Assert.True(header.IsGroupHeader);
        viewModel.SelectedNode = header;

        Assert.NotNull(viewModel.SelectedNode);
        Assert.False(viewModel.SelectedNode.IsGroupHeader);
        Assert.True(header.IsExpanded);
        Assert.Equal(fieldCount, viewModel.FieldRows.Count);
        Assert.True(viewModel.HasSelectedBlock);
        Assert.True(viewModel.CanDelete);
    }

    /// <summary>
    /// Verifies that target plus percent pair values split and recompose through the structured editor.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task PercentPair_SplitsAndRecomposesValueAsync()
    {
        using var viewModel = CreateViewModel();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Upgrade";
        viewModel.NewBlockName = "PairUpgrade";
        viewModel.AddBlockCommand.Execute(null);
        viewModel.NewFieldKey = IniConstants.FieldKeys.ProductionTimeChange;
        viewModel.NewFieldValue = "AmericaCommandCenter -80%";
        viewModel.AddFieldCommand.Execute(null);

        var row = viewModel.FieldRows.First(r => r.Key == IniConstants.FieldKeys.ProductionTimeChange);
        Assert.True(row.IsPercentPair);
        Assert.False(row.IsPlainValue);
        Assert.Equal("AmericaCommandCenter", row.PairTarget);
        Assert.Equal("-80", row.PairPercent);

        row.PairPercent = "-50";

        Assert.Equal("AmericaCommandCenter -50%", row.Value);

        viewModel.UndoCommand.Execute(null);

        Assert.Equal("AmericaCommandCenter -80%", viewModel.FieldRows.First(r => r.Key == IniConstants.FieldKeys.ProductionTimeChange).Value);
    }

    /// <summary>
    /// Verifies that overview cards fall back to per-type icons instead of a generic cube.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task CanvasCard_UsesPerTypeFallbackIconAsync()
    {
        using var viewModel = CreateViewModel();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Upgrade";
        viewModel.NewBlockName = "IconUpgrade";
        viewModel.AddBlockCommand.Execute(null);
        viewModel.ToggleCanvasOverviewCommand.Execute(null);

        var card = viewModel.CanvasBlockCards.First(c => c.Block.Name == "IconUpgrade");
        Assert.Equal("ArrowUpBoldHexagonOutline", card.IconKind);
    }

    /// <summary>
    /// Verifies that overview cards are built lazily when the overview is toggled on.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task CanvasOverview_BuildsCardsOnlyWhenVisibleAsync()
    {
        using var viewModel = CreateViewModel();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Upgrade";
        viewModel.NewBlockName = "HiddenUpgrade";
        viewModel.AddBlockCommand.Execute(null);

        Assert.Empty(viewModel.CanvasBlockCards);

        viewModel.ToggleCanvasOverviewCommand.Execute(null);

        Assert.Single(viewModel.CanvasBlockCards);
        Assert.Equal("Tools.IniEditor.Canvas.OverviewCount", viewModel.CanvasOverviewCountText);
    }

    /// <summary>
    /// Verifies that model and texture fields surface as linked canvas assets.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task SelectedBlock_ExposesLinkedAssetsAsync()
    {
        using var viewModel = CreateViewModel();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Object";
        viewModel.NewBlockName = "AssetObject";
        viewModel.AddBlockCommand.Execute(null);
        viewModel.NewFieldKey = "Model";
        viewModel.NewFieldValue = "AVPaladin";
        viewModel.AddFieldCommand.Execute(null);
        viewModel.NewFieldKey = "ButtonImage";
        viewModel.NewFieldValue = "TestImage";
        viewModel.AddFieldCommand.Execute(null);

        Assert.True(viewModel.HasSelectedBlockAssets);
        Assert.Contains(viewModel.SelectedBlockAssets, asset => asset.Label == "AVPaladin");
        Assert.Contains(viewModel.SelectedBlockAssets, asset => asset.Label == "TestImage");
        Assert.Equal("CubeOutline", viewModel.SelectedBlockIconKind);
    }

    /// <summary>
    /// Verifies that the expanded schema covers audio, particle, and upgrade effect fields.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task SchemaService_CoversExpandedBlockTypesAsync()
    {
        using var viewModel = CreateViewModel();
        await viewModel.NewDocumentCommand.ExecuteAsync(null);
        viewModel.NewBlockType = "Upgrade";
        viewModel.NewBlockName = "SchemaUpgrade";
        viewModel.AddBlockCommand.Execute(null);
        viewModel.NewFieldKey = IniConstants.FieldKeys.ProductionTimeChange;
        viewModel.NewFieldValue = "Obj -10%";
        viewModel.AddFieldCommand.Execute(null);

        var row = viewModel.FieldRows.First(r => r.Key == IniConstants.FieldKeys.ProductionTimeChange);
        Assert.True(row.IsKnown);
        Assert.Equal("Object", row.ReferenceBlockType);
    }
}
