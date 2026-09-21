// <copyright file="BundleItemEditorViewModelTests.cs" company="Enowx Labs">
// Copyright (c) Enowx Labs. All rights reserved.
// </copyright>

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.ViewModels;

using System;
using System.IO;
using GenHub.Core.Interfaces.Common;
using GenHub.Features.Tools.ModBuilder.Services;
using GenHub.Features.Tools.ModBuilder.ViewModels;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for <see cref="BundleItemEditorViewModel"/> match calculation.
/// </summary>
public sealed class BundleItemEditorViewModelTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly Mock<ILocalizationService> _localizationServiceMock = new();

    public BundleItemEditorViewModelTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        WriteFile("GameFilesEdited/Data/INI/a.ini");
        WriteFile("GameFilesEdited/Data/INI/b.ini");
        WriteFile("GameFilesEdited/Data/English/c.csf");

        _localizationServiceMock
            .Setup(l => l.GetString(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, object?[] args) => key);
    }

    [Fact]
    public void RecalculateMatches_CountsGlobMatches()
    {
        var viewModel = new BundleItemEditorViewModel(_localizationServiceMock.Object);
        viewModel.SetPatterns(["GameFilesEdited/Data/INI/**/*.ini"]);

        viewModel.RecalculateMatches(_tempDirectory);

        Assert.Equal(2, viewModel.MatchingFilesCount);
    }

    [Fact]
    public void RecalculateMatches_SharedSnapshot_MatchesDiskWalk()
    {
        var snapshot = ProjectFileSnapshot.Create(_tempDirectory);

        var withSnapshot = new BundleItemEditorViewModel(_localizationServiceMock.Object);
        withSnapshot.SetPatterns(["GameFilesEdited/Data/**/*.ini", "GameFilesEdited/Data/English/c.csf"]);
        withSnapshot.RecalculateMatches(_tempDirectory, snapshot);

        var withoutSnapshot = new BundleItemEditorViewModel(_localizationServiceMock.Object);
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

            var viewModel = new BundleItemEditorViewModel(_localizationServiceMock.Object);
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
        var viewModel = new BundleItemEditorViewModel(_localizationServiceMock.Object);

        viewModel.RecalculateMatches(_tempDirectory);

        Assert.Equal(0, viewModel.MatchingFilesCount);
        Assert.Equal("Tools.ModBuilder.BundleItemEditor.MatchingFiles.NoPatterns", viewModel.MatchingFilesSummary);
    }

    [Fact]
    public void RecalculateMatches_WithMatches_ResolvesLocalizedSummaryWithCount()
    {
        var viewModel = new BundleItemEditorViewModel(_localizationServiceMock.Object);
        viewModel.SetPatterns(["GameFilesEdited/Data/INI/**/*.ini"]);

        viewModel.RecalculateMatches(_tempDirectory);

        Assert.Equal(2, viewModel.MatchingFilesCount);
        _localizationServiceMock.Verify(
            l => l.GetString(
                "Tools.ModBuilder.BundleItemEditor.MatchingFiles.MatchesMany",
                It.Is<object[]>(args => args.Length == 1 && args[0] != null && args[0].Equals(2))),
            Times.Once);

        var singleViewModel = new BundleItemEditorViewModel(_localizationServiceMock.Object);
        singleViewModel.SetPatterns(["GameFilesEdited/Data/English/c.csf"]);

        singleViewModel.RecalculateMatches(_tempDirectory);

        Assert.Equal(1, singleViewModel.MatchingFilesCount);
        _localizationServiceMock.Verify(
            l => l.GetString(
                "Tools.ModBuilder.BundleItemEditor.MatchingFiles.MatchesOne",
                It.Is<object[]>(args => args.Length == 1 && args[0] != null && args[0].Equals(1))),
            Times.Once);
    }

    [Fact]
    public void AddPattern_UsesSnapshotForRecalculation()
    {
        var snapshot = ProjectFileSnapshot.Create(_tempDirectory);
        var viewModel = new BundleItemEditorViewModel(_localizationServiceMock.Object);
        viewModel.SetPatterns(["GameFilesEdited/Data/English/c.csf"], _tempDirectory, snapshot);

        viewModel.AddPattern("GameFilesEdited/Data/INI/**/*.ini", _tempDirectory, snapshot);

        Assert.Equal(3, viewModel.MatchingFilesCount);
    }

    [Fact]
    public void AddPatterns_BulkAdd_RecalculatesOnceAndDedupes()
    {
        var snapshot = ProjectFileSnapshot.Create(_tempDirectory);
        var viewModel = new BundleItemEditorViewModel(_localizationServiceMock.Object);
        viewModel.SetPatterns(["GameFilesEdited/Data/English/c.csf"], _tempDirectory, snapshot);

        viewModel.AddPatterns(
            ["GameFilesEdited/Data/INI/a.ini", "GameFilesEdited/Data/INI/b.ini", "GameFilesEdited/Data/INI/A.INI"],
            _tempDirectory,
            snapshot);

        Assert.Equal(3, viewModel.SourcePatternsList.Count);
        Assert.Equal(3, viewModel.MatchingFilesCount);
    }

    [Fact]
    public void AddPatterns_ReplacesDefaultWildcard()
    {
        var viewModel = new BundleItemEditorViewModel(_localizationServiceMock.Object);
        viewModel.SetPatterns(["GameFilesEdited/**/*.*"]);

        viewModel.AddPatterns(["GameFilesEdited/Data/INI/a.ini"]);

        Assert.Single(viewModel.SourcePatternsList);
        Assert.Equal("GameFilesEdited/Data/INI/a.ini", viewModel.SourcePatternsList[0].Pattern);
    }

    [Fact]
    public void SetPatterns_RaisesSingleCollectionNotification()
    {
        var viewModel = new BundleItemEditorViewModel(_localizationServiceMock.Object);
        var notifications = 0;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BundleItemEditorViewModel.SourcePatternsList))
            {
                notifications++;
            }
        };

        viewModel.SetPatterns(["a.ini", "b.ini", "c.ini"]);

        Assert.Equal(1, notifications);
        Assert.Equal(3, viewModel.SourcePatternsList.Count);
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
