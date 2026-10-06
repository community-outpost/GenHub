using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.UserData.Services;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace GenHub.Tests.Core.Features.UserData;

/// <summary>
/// Unit tests for <see cref="UserDataPathHelper"/>.
/// </summary>
public class UserDataPathHelperTests
{
    /// <summary>
    /// Tests that candidate map names are extracted accurately from manifest files.
    /// </summary>
    [Fact]
    public void ExtractCandidateMapNames_FromManifestFiles_ExtractsDistinctMapNames()
    {
        var files = new[]
        {
            new ManifestFile { RelativePath = @"Maps\DesertStorm\DesertStorm.map", InstallTarget = ContentInstallTarget.UserMapsDirectory },
            new ManifestFile { RelativePath = "DesertStorm.map", InstallTarget = ContentInstallTarget.UserMapsDirectory },
            new ManifestFile { RelativePath = "TwilightFlame.MAP", InstallTarget = ContentInstallTarget.UserMapsDirectory },
            new ManifestFile { RelativePath = "Replays/Game1.rep", InstallTarget = ContentInstallTarget.UserReplaysDirectory },
            new ManifestFile { RelativePath = "DesertStorm.ini", InstallTarget = ContentInstallTarget.UserMapsDirectory },
        };

        var names = UserDataPathHelper.ExtractCandidateMapNames(files);

        Assert.Equal(2, names.Count);
        Assert.Contains("DesertStorm", names);
        Assert.Contains("TwilightFlame", names);
    }

    /// <summary>
    /// Tests that flat map art thumbnails with suffix "_art.tga" are folded to standard map thumbnail paths.
    /// </summary>
    [Fact]
    public void NormalizeUserDataRelativePath_MapsDirectory_FoldsArtTgaCorrectly()
    {
        var normalized = UserDataPathHelper.NormalizeUserDataRelativePath(
            ContentInstallTarget.UserMapsDirectory,
            "BattlePlan_art.tga");

        Assert.Equal("BattlePlan/BattlePlan.tga", normalized);
    }

    /// <summary>
    /// Tests that flat companion files (.ini, .str, .wak) are folded into their corresponding map subfolders.
    /// </summary>
    [Fact]
    public void NormalizeUserDataRelativePath_MapsDirectory_NormalizesFlatCompanionFiles()
    {
        var ini = UserDataPathHelper.NormalizeUserDataRelativePath(
            ContentInstallTarget.UserMapsDirectory,
            "FlatMap.ini");
        var str = UserDataPathHelper.NormalizeUserDataRelativePath(
            ContentInstallTarget.UserMapsDirectory,
            "FlatMap.str");
        var wak = UserDataPathHelper.NormalizeUserDataRelativePath(
            ContentInstallTarget.UserMapsDirectory,
            "FlatMap.wak");

        Assert.Equal("FlatMap/FlatMap.ini", ini);
        Assert.Equal("FlatMap/FlatMap.str", str);
        Assert.Equal("FlatMap/FlatMap.wak", wak);
    }

    /// <summary>
    /// Tests that generic preview images like map.tga fold to the single map name when provided.
    /// </summary>
    [Fact]
    public void NormalizeUserDataRelativePath_MapsDirectory_ResolvesGenericPreviewWithFallbackName()
    {
        var normalized = UserDataPathHelper.NormalizeUserDataRelativePath(
            ContentInstallTarget.UserMapsDirectory,
            "map.tga",
            singleMapBaseName: "CustomArena");

        Assert.Equal("CustomArena/CustomArena.tga", normalized);
    }

    /// <summary>
    /// Tests that leading directory prefixes are stripped for Replays and Screenshots folders.
    /// </summary>
    [Fact]
    public void NormalizeUserDataRelativePath_ReplaysAndScreenshots_StripsLeadingDirectory()
    {
        var replay = UserDataPathHelper.NormalizeUserDataRelativePath(
            ContentInstallTarget.UserReplaysDirectory,
            @"Replays\MyReplay.rep");
        var screenshot = UserDataPathHelper.NormalizeUserDataRelativePath(
            ContentInstallTarget.UserScreenshotsDirectory,
            @"Screenshots/Victory.bmp");

        Assert.Equal("MyReplay.rep", replay);
        Assert.Equal("Victory.bmp", screenshot);
    }

    /// <summary>
    /// Tests that supported map file extensions are recognized correctly.
    /// </summary>
    /// <param name="ext">The file extension.</param>
    /// <param name="expected">Whether it is expected to be supported.</param>
    [Theory]
    [InlineData(".map", true)]
    [InlineData(".MAP", true)]
    [InlineData(".tga", true)]
    [InlineData(".ini", true)]
    [InlineData(".str", true)]
    [InlineData(".wak", true)]
    [InlineData(".txt", false)]
    [InlineData(".exe", false)]
    [InlineData(".big", false)]
    public void IsSupportedMapExtension_ValidatesCorrectly(string ext, bool expected)
    {
        var result = UserDataPathHelper.IsSupportedMapExtension(ext);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Tests that relative paths attempting directory traversal outside the user data root throw an exception.
    /// </summary>
    [Fact]
    public void ResolveUserDataTargetPath_EscapesRoot_ThrowsInvalidOperationException()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "GenHubUserDataTest_" + Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() =>
            UserDataPathHelper.ResolveUserDataTargetPath(
                ContentInstallTarget.UserDataDirectory,
                "../../escaped.txt",
                basePath));
    }

    /// <summary>
    /// Tests that resolving a path within a filesystem root directory succeeds and does not throw an exception.
    /// </summary>
    [Fact]
    public void ResolveUserDataTargetPath_WhenBasePathIsFileSystemRoot_ResolvesPathWithoutThrowing()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var resolved = UserDataPathHelper.ResolveUserDataTargetPath(
            ContentInstallTarget.UserMapsDirectory,
            "MyMap/MyMap.map",
            root);

        Assert.Equal(Path.Combine(root, "Maps", "MyMap", "MyMap.map"), resolved);
    }
}
