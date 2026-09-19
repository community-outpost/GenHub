// <copyright file="BundleItemEditorViewModelTests.cs" company="Enowx Labs">
// Copyright (c) Enowx Labs. All rights reserved.
// </copyright>

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.ViewModels;

using System;
using System.IO;
using GenHub.Features.Tools.ModBuilder.Services;
using GenHub.Features.Tools.ModBuilder.ViewModels;
using Xunit;

/// <summary>
/// Unit tests for <see cref="BundleItemEditorViewModel"/> match calculation.
/// </summary>
public sealed class BundleItemEditorViewModelTests : IDisposable
{
    private readonly string _tempDirectory;

    public BundleItemEditorViewModelTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        WriteFile("GameFilesEdited/Data/INI/a.ini");
        WriteFile("GameFilesEdited/Data/INI/b.ini");
        WriteFile("GameFilesEdited/Data/English/c.csf");
    }

    [Fact]
    public void RecalculateMatches_CountsGlobMatches()
    {
        var viewModel = new BundleItemEditorViewModel();
        viewModel.SetPatterns(["GameFilesEdited/Data/INI/**/*.ini"]);

        viewModel.RecalculateMatches(_tempDirectory);

        Assert.Equal(2, viewModel.MatchingFilesCount);
    }

    [Fact]
    public void RecalculateMatches_SharedSnapshot_MatchesDiskWalk()
    {
        var snapshot = ProjectFileSnapshot.Create(_tempDirectory);

        var withSnapshot = new BundleItemEditorViewModel();
        withSnapshot.SetPatterns(["GameFilesEdited/Data/**/*.ini", "GameFilesEdited/Data/English/c.csf"]);
        withSnapshot.RecalculateMatches(_tempDirectory, snapshot);

        var withoutSnapshot = new BundleItemEditorViewModel();
        withoutSnapshot.SetPatterns(["GameFilesEdited/Data/**/*.ini", "GameFilesEdited/Data/English/c.csf"]);
        withoutSnapshot.RecalculateMatches(_tempDirectory);

        Assert.Equal(withoutSnapshot.MatchingFilesCount, withSnapshot.MatchingFilesCount);
        Assert.Equal(3, withSnapshot.MatchingFilesCount);
    }

    [Fact]
    public void RecalculateMatches_ForeignSnapshot_FallsBackToFreshWalk()
    {
        var otherDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(otherDir);
        try
        {
            var foreignSnapshot = ProjectFileSnapshot.Create(otherDir);

            var viewModel = new BundleItemEditorViewModel();
            viewModel.SetPatterns(["GameFilesEdited/Data/INI/**/*.ini"]);
            viewModel.RecalculateMatches(_tempDirectory, foreignSnapshot);

            Assert.Equal(2, viewModel.MatchingFilesCount);
        }
        finally
        {
            Directory.Delete(otherDir, true);
        }
    }

    [Fact]
    public void RecalculateMatches_NoPatterns_ReportsEmpty()
    {
        var viewModel = new BundleItemEditorViewModel();

        viewModel.RecalculateMatches(_tempDirectory);

        Assert.Equal(0, viewModel.MatchingFilesCount);
        Assert.Equal("No patterns defined", viewModel.MatchingFilesSummary);
    }

    [Fact]
    public void AddPattern_UsesSnapshotForRecalculation()
    {
        var snapshot = ProjectFileSnapshot.Create(_tempDirectory);
        var viewModel = new BundleItemEditorViewModel();
        viewModel.SetPatterns(["GameFilesEdited/Data/English/c.csf"], _tempDirectory, snapshot);

        viewModel.AddPattern("GameFilesEdited/Data/INI/**/*.ini", _tempDirectory, snapshot);

        Assert.Equal(3, viewModel.MatchingFilesCount);
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
}
