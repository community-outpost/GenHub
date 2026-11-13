// <copyright file="ProjectFileSnapshotTests.cs" company="Enowx Labs">
// Copyright (c) Enowx Labs. All rights reserved.
// </copyright>

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenHub.Features.Tools.ModBuilder.Services;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Xunit;

/// <summary>
/// Unit tests for <see cref="ProjectFileSnapshot"/>.
/// </summary>
public sealed class ProjectFileSnapshotTests : IDisposable
{
    private readonly string _tempDirectory;

    public ProjectFileSnapshotTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        WriteFile("GameFilesEdited/Data/INI/a.ini");
        WriteFile("GameFilesEdited/Data/INI/b.ini");
        WriteFile("GameFilesEdited/Data/English/c.csf");
        WriteFile("GameFilesEdited/Art/Textures/d.tga");
        WriteFile("config/ModBundleItems.json");
        WriteFile("README.txt");
    }

    [Fact]
    public void Create_EnumeratesAllFilesOnce()
    {
        var snapshot = ProjectFileSnapshot.Create(_tempDirectory);

        Assert.Equal(6, snapshot.RelativeFilePaths.Count);
        Assert.Contains("GameFilesEdited/Data/INI/a.ini", snapshot.RelativeFilePaths);
        Assert.DoesNotContain(snapshot.RelativeFilePaths, p => p.Contains('\\'));
    }

    [Theory]
    [InlineData("GameFilesEdited/**/*.*")]
    [InlineData("GameFilesEdited/**/*")]
    [InlineData("GameFilesEdited/Data/INI/**/*.ini")]
    [InlineData("Data/INI/**/*.ini")]
    [InlineData("GameFilesEdited/Data/English/c.csf")]
    [InlineData("**/*.tga")]
    [InlineData("**/*.xyz")]
    [InlineData("config/ModBundleItems.json")]
    public void MatchFiles_EqualsDiskMatcherResults(string pattern)
    {
        var snapshot = ProjectFileSnapshot.Create(_tempDirectory);

        var expected = MatchOnDisk([pattern]);
        var actual = snapshot.MatchFiles([pattern]);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void MatchFiles_MultiplePatterns_UnionOfMatches()
    {
        var snapshot = ProjectFileSnapshot.Create(_tempDirectory);

        var actual = snapshot.MatchFiles(["GameFilesEdited/Data/INI/**/*.ini", "**/*.tga"]);

        Assert.Equal(
            new HashSet<string>(
                ["GameFilesEdited/Data/INI/a.ini", "GameFilesEdited/Data/INI/b.ini", "GameFilesEdited/Art/Textures/d.tga"],
                StringComparer.OrdinalIgnoreCase),
            actual);
    }

    [Fact]
    public void CountMatches_ReturnsMatchedFileCount()
    {
        var snapshot = ProjectFileSnapshot.Create(_tempDirectory);

        Assert.Equal(2, snapshot.CountMatches(["GameFilesEdited/Data/INI/**/*.ini"]));
        Assert.Equal(0, snapshot.CountMatches(["**/*.xyz"]));
    }

    [Fact]
    public void MatchFiles_IgnoresBlankPatterns()
    {
        var snapshot = ProjectFileSnapshot.Create(_tempDirectory);

        var actual = snapshot.MatchFiles(["  ", "GameFilesEdited/Data/English/c.csf"]);

        Assert.Single(actual);
    }

    [Fact]
    public void IsSameRoot_MatchesEquivalentPaths()
    {
        var snapshot = ProjectFileSnapshot.Create(_tempDirectory);

        Assert.True(snapshot.IsSameRoot(_tempDirectory));
        Assert.True(snapshot.IsSameRoot(_tempDirectory + Path.DirectorySeparatorChar));
        Assert.True(snapshot.IsSameRoot(_tempDirectory.ToUpperInvariant()));
        Assert.False(snapshot.IsSameRoot(null));
        Assert.False(snapshot.IsSameRoot(string.Empty));
        Assert.False(snapshot.IsSameRoot(Path.Combine(_tempDirectory, "other")));
    }

    [Fact]
    public void Create_MissingDirectory_ReturnsEmptySnapshot()
    {
        var missing = Path.Combine(_tempDirectory, "does-not-exist");

        var snapshot = ProjectFileSnapshot.Create(missing);

        Assert.Empty(snapshot.RelativeFilePaths);
        Assert.Empty(snapshot.MatchFiles(["**/*.*"]));
    }

    [Fact]
    public void MatchFiles_MixedLiteralsAndGlobs_EqualsDiskMatcherResults()
    {
        var snapshot = ProjectFileSnapshot.Create(_tempDirectory);
        string[] patterns =
        [
            "GameFilesEdited/Data/INI/a.ini",
            "GameFilesEdited/Data/English/c.csf",
            "Data/INI/*.ini",
            "config/ModBundleItems.json",
            "missing/file.txt",
        ];

        var expected = MatchOnDisk(patterns);
        var actual = snapshot.MatchFiles(patterns);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("GameFilesEdited/Data/INI/a.ini", false)]
    [InlineData("config/ModBundleItems.json", false)]
    [InlineData("GameFilesEdited/**/*.*", true)]
    [InlineData("**/*.ini", true)]
    [InlineData("Data/INI/a?.ini", true)]
    [InlineData("Data/INI/[ab].ini", true)]
    public void IsGlobPattern_ClassifiesPatterns(string pattern, bool expected)
    {
        Assert.Equal(expected, ProjectFileSnapshot.IsGlobPattern(pattern));
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

    private HashSet<string> MatchOnDisk(string[] patterns)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        foreach (var rawPattern in patterns)
        {
            var pattern = rawPattern.TrimStart('/', '\\').Replace('\\', '/');
            matcher.AddInclude(pattern);
            if (!pattern.StartsWith("GameFilesEdited/", StringComparison.OrdinalIgnoreCase))
            {
                matcher.AddInclude($"GameFilesEdited/{pattern}");
            }
        }

        var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(_tempDirectory)));
        return result.Files
            .Select(f => f.Path.Replace('\\', '/').TrimStart('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
