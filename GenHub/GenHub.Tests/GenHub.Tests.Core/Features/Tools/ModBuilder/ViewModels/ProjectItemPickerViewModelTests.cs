// <copyright file="ProjectItemPickerViewModelTests.cs" company="Enowx Labs">
// Copyright (c) Enowx Labs. All rights reserved.
// </copyright>

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.ViewModels;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GenHub.Features.Tools.ModBuilder.Models;
using GenHub.Features.Tools.ModBuilder.ViewModels;
using Xunit;

/// <summary>
/// Unit tests for <see cref="ProjectItemPickerViewModel"/> selection and pattern generation.
/// </summary>
public sealed class ProjectItemPickerViewModelTests : IDisposable
{
    private readonly string _tempDirectory;

    public ProjectItemPickerViewModelTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        WriteFile("GameFilesEdited/Data/INI/a.ini");
        WriteFile("GameFilesEdited/Data/INI/b.ini");
        WriteFile("GameFilesEdited/Data/English/c.csf");
        WriteFile("GameFilesEdited/Art/Textures/d.tga");
        WriteFile("config/ModBundleItems.json");
    }

    [Fact]
    public void Constructor_DoesNotBuildTreeSynchronously()
    {
        var viewModel = new ProjectItemPickerViewModel(_tempDirectory, ["GameFilesEdited/Data/INI/**/*.ini"]);

        Assert.True(viewModel.IsLoading);
        Assert.Empty(viewModel.Nodes);
    }

    [Fact]
    public async Task InitializeAsync_ExtensionGlob_PreselectsMatchingFilesAsync()
    {
        var viewModel = new ProjectItemPickerViewModel(_tempDirectory, ["GameFilesEdited/Data/INI/**/*.ini"]);

        await viewModel.InitializeAsync();

        Assert.False(viewModel.IsLoading);
        Assert.True(FindNode(viewModel, "GameFilesEdited/Data/INI/a.ini")!.IsSelected);
        Assert.True(FindNode(viewModel, "GameFilesEdited/Data/INI/b.ini")!.IsSelected);
        Assert.False(FindNode(viewModel, "GameFilesEdited/Data/English/c.csf")!.IsSelected);
        Assert.False(FindNode(viewModel, "GameFilesEdited/Art/Textures/d.tga")!.IsSelected);
        Assert.Equal("Selected: 2 files", viewModel.SelectionSummary);
    }

    [Fact]
    public async Task InitializeAsync_ExtensionGlob_ExpandsAncestorsOfMatchesAsync()
    {
        var viewModel = new ProjectItemPickerViewModel(_tempDirectory, ["GameFilesEdited/Data/INI/**/*.ini"]);

        await viewModel.InitializeAsync();

        Assert.True(FindNode(viewModel, "GameFilesEdited")!.IsExpanded);
        Assert.True(FindNode(viewModel, "GameFilesEdited/Data")!.IsExpanded);
        Assert.True(FindNode(viewModel, "GameFilesEdited/Data/INI")!.IsExpanded);
    }

    [Fact]
    public async Task InitializeAsync_DirectoryGlob_PreselectsAndExpandsDirectoryAsync()
    {
        var viewModel = new ProjectItemPickerViewModel(_tempDirectory, ["GameFilesEdited/Art/Textures/**/*.*"]);

        await viewModel.InitializeAsync();

        var dirNode = FindNode(viewModel, "GameFilesEdited/Art/Textures")!;
        Assert.True(dirNode.IsSelected);
        Assert.True(dirNode.IsExpanded);
        Assert.True(FindNode(viewModel, "GameFilesEdited/Art/Textures/d.tga")!.IsSelected);
        Assert.False(FindNode(viewModel, "GameFilesEdited/Data/INI/a.ini")!.IsSelected);
    }

    [Fact]
    public async Task InitializeAsync_WholeProjectGlob_PreselectsRootAndAllFilesAsync()
    {
        var viewModel = new ProjectItemPickerViewModel(_tempDirectory, ["GameFilesEdited/**/*"]);

        await viewModel.InitializeAsync();

        Assert.True(FindNode(viewModel, "GameFilesEdited")!.IsSelected);
        Assert.True(FindNode(viewModel, "GameFilesEdited/Data/INI/a.ini")!.IsSelected);
        Assert.True(FindNode(viewModel, "GameFilesEdited/Data/English/c.csf")!.IsSelected);
        Assert.True(FindNode(viewModel, "GameFilesEdited/Art/Textures/d.tga")!.IsSelected);
    }

    [Fact]
    public async Task InitializeAsync_NoPatterns_SelectsNothingAsync()
    {
        var viewModel = new ProjectItemPickerViewModel(_tempDirectory);

        await viewModel.InitializeAsync();

        Assert.False(viewModel.HasSelection);
        Assert.Equal("No items selected", viewModel.SelectionSummary);
        Assert.Empty(viewModel.GetGeneratedPatterns());
    }

    [Fact]
    public async Task GetGeneratedPatterns_UnchangedSelection_ReturnsOriginalPatternsAsync()
    {
        var original = new List<string> { "GameFilesEdited/Data/INI/**/*.ini", "GameFilesEdited/Art/Textures/d.tga" };
        var viewModel = new ProjectItemPickerViewModel(_tempDirectory, original);

        await viewModel.InitializeAsync();
        var generated = viewModel.GetGeneratedPatterns();

        Assert.Equal(original, generated);
    }

    [Fact]
    public async Task GetGeneratedPatterns_UncheckedFile_ExplodesDirectoryGlobAsync()
    {
        var viewModel = new ProjectItemPickerViewModel(_tempDirectory, ["GameFilesEdited/Data/INI/**/*.*"]);

        await viewModel.InitializeAsync();
        FindNode(viewModel, "GameFilesEdited/Data/INI/a.ini")!.IsSelected = false;

        var generated = viewModel.GetGeneratedPatterns();

        Assert.DoesNotContain("GameFilesEdited/Data/INI/**/*.*", generated);
        Assert.Contains("GameFilesEdited/Data/INI/b.ini", generated);
        Assert.DoesNotContain("GameFilesEdited/Data/INI/a.ini", generated);
    }

    [Fact]
    public async Task GetGeneratedPatterns_UncheckedDirectory_RemovesItsGlobAsync()
    {
        var viewModel = new ProjectItemPickerViewModel(_tempDirectory, ["GameFilesEdited/Art/Textures/**/*.*"]);

        await viewModel.InitializeAsync();
        FindNode(viewModel, "GameFilesEdited/Art/Textures")!.IsSelected = false;
        FindNode(viewModel, "GameFilesEdited/Art/Textures/d.tga")!.IsSelected = false;

        var generated = viewModel.GetGeneratedPatterns();

        Assert.Empty(generated);
    }

    [Fact]
    public async Task GetGeneratedPatterns_PureDirectorySelection_EmitsDirectoryGlobAsync()
    {
        var viewModel = new ProjectItemPickerViewModel(_tempDirectory);

        await viewModel.InitializeAsync();
        FindNode(viewModel, "GameFilesEdited/Data/INI")!.IsSelected = true;

        var generated = viewModel.GetGeneratedPatterns();

        Assert.Equal(["GameFilesEdited/Data/INI/**/*.*"], generated);
    }

    [Fact]
    public async Task GetGeneratedPatterns_OutOfTreePattern_IsPreservedThroughEditsAsync()
    {
        var viewModel = new ProjectItemPickerViewModel(
            _tempDirectory,
            ["GameFilesEdited/Data/INI/**/*.ini", "config/ModBundleItems.json"]);

        await viewModel.InitializeAsync();
        FindNode(viewModel, "GameFilesEdited/Data/INI/a.ini")!.IsSelected = false;

        var generated = viewModel.GetGeneratedPatterns();

        Assert.Contains("config/ModBundleItems.json", generated);
        Assert.Contains("GameFilesEdited/Data/INI/b.ini", generated);
        Assert.DoesNotContain("GameFilesEdited/Data/INI/a.ini", generated);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    private void WriteFile(string relativePath)
    {
        var fullPath = Path.Combine(_tempDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "test");
    }

    private static FileTreeNode? FindNode(ProjectItemPickerViewModel viewModel, string relativePath)
    {
        return FindNodeRecursive(viewModel.Nodes, relativePath.Replace('\\', '/'));
    }

    private static FileTreeNode? FindNodeRecursive(IEnumerable<FileTreeNode> nodes, string relativePath)
    {
        foreach (var node in nodes)
        {
            if (node.RelativePath.Replace('\\', '/').Equals(relativePath, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            var found = FindNodeRecursive(node.Children, relativePath);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
