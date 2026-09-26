using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Features.Info.ViewModels;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.Features.Info;

/// <summary>
/// Unit tests for the interactive tool demo view models.
/// </summary>
public class ToolDemoViewModelTests
{
    /// <summary>
    /// Verifies that the WND editor demo seeds placeholder documents.
    /// </summary>
    [Fact]
    public void WndEditorDemo_Constructor_SeedsPlaceholders()
    {
        var viewModel = new WndEditorDemoViewModel();

        viewModel.Files.Should().Contain(["MainMenu.wnd", "OptionsMenu.wnd"]);
        viewModel.RootNodes.Should().ContainSingle();
        viewModel.CanvasNodes.Should().HaveCount(6);
        viewModel.SelectedNode.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that validating the placeholder document reports zero errors.
    /// </summary>
    [Fact]
    public void WndEditorDemo_ValidateDocument_ReportsZeroErrors()
    {
        var viewModel = new WndEditorDemoViewModel();

        viewModel.ValidateDocumentCommand.Execute(null);

        viewModel.ValidationMessage.Should().Contain("0 errors");
    }

    /// <summary>
    /// Verifies that adding and deleting a child window updates the node count.
    /// </summary>
    [Fact]
    public void WndEditorDemo_AddAndDeleteChild_UpdatesNodeCount()
    {
        var viewModel = new WndEditorDemoViewModel();

        viewModel.AddChildWindowCommand.Execute(null);
        viewModel.NodeCount.Should().Be(7);

        viewModel.DeleteSelectedWindowCommand.Execute(null);
        viewModel.NodeCount.Should().Be(6);
    }

    /// <summary>
    /// Verifies that the ModBuilder demo seeds packs and a manifest preview.
    /// </summary>
    [Fact]
    public void ModBuilderDemo_Constructor_SeedsPacksAndPreview()
    {
        var viewModel = new ModBuilderDemoViewModel();

        viewModel.BundlePacks.Should().HaveCount(3);
        viewModel.TotalBundledFiles.Should().Be(4);
        viewModel.ManifestPreview.Should().Contain("1.0.demo.mod.shockwavedemo");
        viewModel.ManifestPreview.Should().Contain("1080p");
    }

    /// <summary>
    /// Verifies that adding a duplicate file to a pack is ignored.
    /// </summary>
    [Fact]
    public void ModBuilderDemo_AddDuplicateFile_IsIgnored()
    {
        var viewModel = new ModBuilderDemoViewModel();
        viewModel.SelectedPack = viewModel.BundlePacks[0];
        viewModel.SelectedAvailableFile = viewModel.AvailableFiles[1];

        viewModel.AddFileToPackCommand.Execute(null);
        viewModel.AddFileToPackCommand.Execute(null);

        viewModel.BundlePacks[0].Items.Should().HaveCount(3);
    }

    /// <summary>
    /// Verifies that building a pack writes simulated log lines.
    /// </summary>
    [Fact]
    public void ModBuilderDemo_BuildPack_WritesBuildLog()
    {
        var viewModel = new ModBuilderDemoViewModel();

        viewModel.BuildPackCommand.Execute(null);

        viewModel.BuildLog.Should().NotBeEmpty();
        viewModel.BuildLog.Last().Should().Contain("Build complete");
    }

    /// <summary>
    /// Verifies that the learn action requests navigation to the manifests section.
    /// </summary>
    [Fact]
    public void ModBuilderDemo_LearnAboutManifests_RequestsManifestsSection()
    {
        var viewModel = new ModBuilderDemoViewModel();
        string? requested = null;
        viewModel.NavigationRequested = sectionId => requested = sectionId;

        viewModel.LearnAboutManifestsCommand.Execute(null);

        requested.Should().Be(InfoConstants.SectionContentManifests);
    }

    /// <summary>
    /// Verifies that the Hotkey Editor demo seeds a conflicting placeholder card.
    /// </summary>
    [Fact]
    public void HotkeyEditorDemo_Constructor_SeedsConflictingCard()
    {
        var viewModel = new HotkeyEditorDemoViewModel();

        viewModel.Slots.Should().HaveCount(12);
        viewModel.HasConflicts.Should().BeTrue();
        viewModel.ConflictCount.Should().Be(2);
    }

    /// <summary>
    /// Verifies that assigning a distinct key clears the seeded conflict.
    /// </summary>
    [Fact]
    public void HotkeyEditorDemo_AssignDistinctKey_ClearsConflict()
    {
        var viewModel = new HotkeyEditorDemoViewModel();
        viewModel.SelectedSlot = viewModel.Slots.Last();

        viewModel.AssignKeyCommand.Execute("V");

        viewModel.HasConflicts.Should().BeFalse();
        viewModel.StatusMessage.Should().Contain("No conflicts");
    }
}
