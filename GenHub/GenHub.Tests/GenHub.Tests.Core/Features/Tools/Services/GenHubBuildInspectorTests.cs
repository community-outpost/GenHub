using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Services.Tools;
using System;
using System.IO;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Unit tests for <see cref="GenHubBuildInspector"/>.
/// </summary>
public class GenHubBuildInspectorTests
{
    private readonly IGenHubBuildInspector _inspector = new GenHubBuildInspector();

    /// <summary>
    /// Verifies that paths containing GenHub build extensions and name patterns are identified.
    /// </summary>
    /// <param name="path">The file path to test.</param>
    /// <param name="expected">Expected detection result.</param>
    [Theory]
    [InlineData("C:\\Releases\\GenHub-Setup-1.0.0.exe", true)]
    [InlineData("/tmp/GenHub.Core-1.2.3.nupkg", true)]
    [InlineData("D:\\Builds\\genhub-v2.0.0-win-x64.zip", true)]
    [InlineData("GenHub-Nightly.7z", true)]
    [InlineData("C:\\Games\\Command and Conquer Generals\\generals.exe", false)]
    [InlineData("C:\\Games\\Zero Hour\\game.dat", false)]
    [InlineData("C:\\Apps\\Discord.exe", false)]
    [InlineData("", false)]
    public void IsGenHubBuildPath_DetectsAppropriately(string path, bool expected)
    {
        var result = _inspector.IsGenHubBuildPath(path);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that release naming produces correct version and suggested category.
    /// </summary>
    [Fact]
    public void Inspect_ReleaseArtifact_InfersReleaseMetadata()
    {
        var info = _inspector.Inspect("C:\\Builds\\GenHub-Setup-1.5.2.exe");

        Assert.True(info.IsGenHubBuild);
        Assert.Equal("1.5.2", info.Version);
        Assert.Equal(GenHubBuildConstants.CategoryRelease, info.SuggestedCategory);
        Assert.Equal(GenHubBuildConstants.ChannelRelease, info.BuildChannel);
        Assert.Null(info.PullRequestNumber);
        Assert.False(info.IsCustomBuild);
    }

    /// <summary>
    /// Verifies that PR builds are categorized as Test and extract the PR number.
    /// </summary>
    [Fact]
    public void Inspect_PullRequestArtifact_InfersTestCategoryAndPrNumber()
    {
        var info = _inspector.Inspect("/builds/GenHub-PR-123-1.0.0-dev.45.exe");

        Assert.True(info.IsGenHubBuild);
        Assert.Equal("1.0.0-dev.45", info.Version);
        Assert.Equal(GenHubBuildConstants.CategoryTest, info.SuggestedCategory);
        Assert.Equal(GenHubBuildConstants.ChannelPr, info.BuildChannel);
        Assert.Equal(123, info.PullRequestNumber);
    }

    /// <summary>
    /// Verifies that development / nightly builds are categorized as Dev.
    /// </summary>
    [Fact]
    public void Inspect_DevBuildArtifact_InfersDevCategory()
    {
        var info = _inspector.Inspect("C:\\CI\\genhub-dev-build-456.zip");

        Assert.True(info.IsGenHubBuild);
        Assert.Equal(GenHubBuildConstants.CategoryDev, info.SuggestedCategory);
        Assert.Equal(GenHubBuildConstants.ChannelDev, info.BuildChannel);
    }

    /// <summary>
    /// Verifies that nightly builds are categorized as Dev with prerelease flag.
    /// </summary>
    [Fact]
    public void Inspect_NightlyBuildArtifact_InfersDevCategory()
    {
        var info = _inspector.Inspect(@"C:\CI\GenHub-Nightly.zip");

        Assert.True(info.IsGenHubBuild);
        Assert.Equal(GenHubBuildConstants.CategoryDev, info.SuggestedCategory);
        Assert.Equal(GenHubBuildConstants.ChannelDev, info.BuildChannel);
        Assert.True(GenHubBuildConstants.IsPrereleaseChannel(info.BuildChannel));
    }

    /// <summary>
    /// Verifies that community forks are categorized as CustomFork.
    /// </summary>
    [Fact]
    public void Inspect_ForkArtifact_InfersCustomForkCategory()
    {
        var info = _inspector.Inspect("D:\\Downloads\\GenHub-Community-Fork-v1.0.0.zip");

        Assert.True(info.IsGenHubBuild);
        Assert.Equal("1.0.0", info.Version);
        Assert.Equal(GenHubBuildConstants.CategoryCustomFork, info.SuggestedCategory);
        Assert.True(info.IsCustomBuild || info.IsFork);
    }

    /// <summary>
    /// Verifies that non-GenHub files report IsGenHubBuild as false and Unknown version.
    /// </summary>
    [Fact]
    public void Inspect_NonGenHubFile_ReturnsFalse()
    {
        var info = _inspector.Inspect("C:\\Games\\generals.exe");

        Assert.False(info.IsGenHubBuild);
        Assert.Equal(GenHubBuildConstants.UnknownVersion, info.Version);
        Assert.Equal(GenHubBuildConstants.CategoryRelease, info.SuggestedCategory);
    }

    /// <summary>
    /// Verifies that a non-existent file does not throw and falls back cleanly.
    /// </summary>
    [Fact]
    public void Inspect_NonExistentFile_DoesNotThrow()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), "GenHub-Setup-9.9.9-doesnotexist.exe");
        var info = _inspector.Inspect(fakePath);

        Assert.True(info.IsGenHubBuild);
        Assert.Equal("9.9.9", info.Version);
        Assert.Equal(GenHubBuildConstants.CategoryRelease, info.SuggestedCategory);
    }

    /// <summary>
    /// Verifies that bare genhub tag is not treated as a GenHub application build.
    /// </summary>
    [Fact]
    public void IsGenHubApplicationBuild_BareGenHubTag_ReturnsFalse()
    {
        var result = GenHubBuildInspector.IsGenHubApplicationBuild(ContentType.Mod, ["genhub"]);
        Assert.False(result);
    }

    /// <summary>
    /// Verifies that genhub-build tag is treated as a GenHub application build.
    /// </summary>
    [Fact]
    public void IsGenHubApplicationBuild_GenHubBuildTag_ReturnsTrue()
    {
        var result = GenHubBuildInspector.IsGenHubApplicationBuild(ContentType.Mod, [GenHubBuildConstants.GenHubBuildTag]);
        Assert.True(result);
    }

    /// <summary>
    /// Verifies that builder tools without the build tag are not treated as GenHub application builds.
    /// </summary>
    [Fact]
    public void IsGenHubApplicationBuild_ToolTypeWithoutTag_ReturnsFalse()
    {
        var result = GenHubBuildInspector.IsGenHubApplicationBuild(ContentType.ModdingTool, []);
        Assert.False(result);
    }

    /// <summary>
    /// Verifies that ContentType.GenHubBuild is treated as a GenHub application build.
    /// </summary>
    [Fact]
    public void IsGenHubApplicationBuild_ContentTypeGenHubBuild_ReturnsTrue()
    {
        var result = GenHubBuildInspector.IsGenHubApplicationBuild(ContentType.GenHubBuild, []);
        Assert.True(result);
    }

    /// <summary>
    /// Verifies that inspecting a synthetic zip archive extracts nuspec metadata and entry point correctly.
    /// </summary>
    [Fact]
    public void Inspect_SyntheticZipArchive_InfersVersionAndEntryPoint()
    {
        var tempZip = Path.Combine(Path.GetTempPath(), $"genhub_test_{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = System.IO.Compression.ZipFile.Open(tempZip, System.IO.Compression.ZipArchiveMode.Create))
            {
                var nuspecEntry = archive.CreateEntry("package.nuspec");
                using (var writer = new StreamWriter(nuspecEntry.Open()))
                {
                    writer.Write("<package><metadata><id>GenHub</id><version>2.5.0</version></metadata></package>");
                }

                var exeEntry = archive.CreateEntry("bin/GenHub.exe");
                using (var stream = exeEntry.Open())
                {
                    stream.WriteByte(0x4D); // "MZ" header
                    stream.WriteByte(0x5A);
                }
            }

            var info = _inspector.Inspect(tempZip);

            Assert.True(info.IsGenHubBuild);
            Assert.Equal("2.5.0", info.Version);
            Assert.Equal("bin/GenHub.exe", info.EntryPoint);
        }
        finally
        {
            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }
        }
    }

    /// <summary>
    /// Verifies that inspecting a synthetic zip archive containing GenHub.dll before GenHub.Windows.exe prefers the executable entry point.
    /// </summary>
    [Fact]
    public void Inspect_SyntheticZipArchive_PrefersExecutableOverManagedAssembly()
    {
        var tempZip = Path.Combine(Path.GetTempPath(), $"genhub_test_{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = System.IO.Compression.ZipFile.Open(tempZip, System.IO.Compression.ZipArchiveMode.Create))
            {
                var dllEntry = archive.CreateEntry("bin/GenHub.dll");
                using (var stream = dllEntry.Open())
                {
                    stream.WriteByte(0x4D);
                    stream.WriteByte(0x5A);
                }

                var exeEntry = archive.CreateEntry("bin/GenHub.Windows.exe");
                using (var stream = exeEntry.Open())
                {
                    stream.WriteByte(0x4D);
                    stream.WriteByte(0x5A);
                }
            }

            var info = _inspector.Inspect(tempZip);

            Assert.True(info.IsGenHubBuild);
            Assert.Equal("bin/GenHub.Windows.exe", info.EntryPoint);
        }
        finally
        {
            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }
        }
    }

    /// <summary>
    /// Verifies that filename-only inspection classifies a beta suffix as the test channel and prerelease.
    /// </summary>
    [Fact]
    public void Inspect_FileNameWithBetaSuffix_IdentifiesAsTestChannel()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), "GenHub-v1.2.3-beta.zip");

        var info = _inspector.Inspect(fakePath);

        Assert.True(info.IsGenHubBuild);
        Assert.Equal("1.2.3-beta", info.Version);
        Assert.Equal(GenHubBuildConstants.ChannelTest, info.BuildChannel);
        Assert.True(GenHubBuildConstants.IsPrereleaseChannel(info.BuildChannel));
    }

    /// <summary>
    /// Verifies that inspecting a community build zip containing an executable without company or channel metadata
    /// infers custom fork identity from the archive filename.
    /// </summary>
    [Fact]
    public void Inspect_CommunityZipWithoutMetadata_InfersCustomForkIdentity()
    {
        var tempZip = Path.Combine(Path.GetTempPath(), $"GenHub-community-1.0.0_{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = System.IO.Compression.ZipFile.Open(tempZip, System.IO.Compression.ZipArchiveMode.Create))
            {
                var exeEntry = archive.CreateEntry("GenHub.exe");
                using (var stream = exeEntry.Open())
                {
                    stream.WriteByte(0x4D);
                    stream.WriteByte(0x5A);
                }
            }

            var info = _inspector.Inspect(tempZip);

            Assert.True(info.IsGenHubBuild);
            Assert.True(info.IsCustomBuild);
            Assert.Equal(GenHubBuildConstants.ChannelCustomFork, info.BuildChannel);
            Assert.Equal(GenHubBuildConstants.CategoryCustomFork, info.SuggestedCategory);
            Assert.NotEqual(GenHubBuildConstants.OfficialContentId, info.SuggestedContentId);
        }
        finally
        {
            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }
        }
    }

    /// <summary>
    /// Verifies that filename-only inspection preserves a nightly prerelease suffix in the parsed version and classifies as Dev channel.
    /// </summary>
    [Fact]
    public void Inspect_FileNameWithNightlySuffix_PreservesNightlyVersionAndIdentifiesDevChannel()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), "GenHub-v1.2.3-nightly.42.zip");

        var info = _inspector.Inspect(fakePath);

        Assert.True(info.IsGenHubBuild);
        Assert.Equal("1.2.3-nightly.42", info.Version);
        Assert.Equal(GenHubBuildConstants.ChannelDev, info.BuildChannel);
        Assert.True(GenHubBuildConstants.IsPrereleaseChannel(info.BuildChannel));
        Assert.False(info.IsCustomBuild);
        Assert.False(info.IsFork);
    }

    /// <summary>
    /// Verifies that an archive with a non-matching filename but with nuspec id GenHub and a renamed executable
    /// is recognized as a GenHub build preserving nuspec version and authors.
    /// </summary>
    [Fact]
    public void Inspect_ArchiveWithGenHubNuspecAndRenamedExe_IdentifiesAsGenHubBuild()
    {
        var tempNupkg = Path.Combine(Path.GetTempPath(), $"package_{Guid.NewGuid():N}.nupkg");
        try
        {
            using (var archive = System.IO.Compression.ZipFile.Open(tempNupkg, System.IO.Compression.ZipArchiveMode.Create))
            {
                var nuspecEntry = archive.CreateEntry("package.nuspec");
                using (var stream = nuspecEntry.Open())
                using (var writer = new StreamWriter(stream, System.Text.Encoding.UTF8))
                {
                    writer.Write("""
                        <?xml version="1.0" encoding="utf-8"?>
                        <package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
                            <metadata>
                                <id>GenHub</id>
                                <version>2.5.0</version>
                                <authors>CommunityForkAuthor</authors>
                                <title>GenHub Fork</title>
                            </metadata>
                        </package>
                        """);
                }

                var exeEntry = archive.CreateEntry("CustomGenApp.exe");
                using (var stream = exeEntry.Open())
                {
                    stream.WriteByte(0x4D);
                    stream.WriteByte(0x5A);
                }
            }

            var info = _inspector.Inspect(tempNupkg);

            Assert.True(info.IsGenHubBuild);
            Assert.Equal("2.5.0", info.Version);
            Assert.Equal("CustomGenApp.exe", info.EntryPoint);
            Assert.True(info.IsFork);
            Assert.True(info.IsCustomBuild);
            Assert.Equal("CommunityForkAuthor", info.ForkName);
            Assert.Equal(GenHubBuildConstants.ChannelCustomFork, info.BuildChannel);
        }
        finally
        {
            if (File.Exists(tempNupkg))
            {
                File.Delete(tempNupkg);
            }
        }
    }

    /// <summary>
    /// Verifies that an archive with a release filename pattern but with a nuspec prerelease version
    /// resolves to a prerelease test channel across alpha, beta, rc, and preview prerelease tags.
    /// </summary>
    /// <param name="prereleaseVersion">The prerelease version string to inspect.</param>
    [Theory]
    [InlineData("1.2.3-alpha")]
    [InlineData("1.2.3-beta")]
    [InlineData("1.2.3-rc.1")]
    [InlineData("1.2.3-preview")]
    public void Inspect_ArchiveWithReleaseNameAndNuspecPrereleaseVersion_ResolvesPrereleaseChannel(string prereleaseVersion)
    {
        var tempZip = Path.Combine(Path.GetTempPath(), $"GenHub-Setup_{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = System.IO.Compression.ZipFile.Open(tempZip, System.IO.Compression.ZipArchiveMode.Create))
            {
                var nuspecEntry = archive.CreateEntry("GenHub.nuspec");
                using (var stream = nuspecEntry.Open())
                using (var writer = new StreamWriter(stream, System.Text.Encoding.UTF8))
                {
                    writer.Write($"""
                        <?xml version="1.0" encoding="utf-8"?>
                        <package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
                            <metadata>
                                <id>GenHub</id>
                                <version>{prereleaseVersion}</version>
                                <authors>Community Outpost</authors>
                            </metadata>
                        </package>
                        """);
                }
            }

            var info = _inspector.Inspect(tempZip);

            Assert.True(info.IsGenHubBuild);
            Assert.Equal(prereleaseVersion, info.Version);
            Assert.Equal(GenHubBuildConstants.ChannelTest, info.BuildChannel);
            Assert.True(GenHubBuildConstants.IsPrereleaseChannel(info.BuildChannel));
        }
        finally
        {
            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }
        }
    }

    /// <summary>
    /// Verifies that an archive with a GenHub filename pattern but a non-GenHub nuspec id
    /// exercises InspectFromFileNameWithNuspec and resolves the prerelease channel from nuspec version.
    /// </summary>
    [Fact]
    public void Inspect_ArchiveWithGenHubFileNameAndNonGenHubNuspecId_ResolvesChannelFromVersion()
    {
        var tempZip = Path.Combine(Path.GetTempPath(), $"GenHub-release-1.5.0_{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = System.IO.Compression.ZipFile.Open(tempZip, System.IO.Compression.ZipArchiveMode.Create))
            {
                var nuspecEntry = archive.CreateEntry("addon.nuspec");
                using (var stream = nuspecEntry.Open())
                using (var writer = new StreamWriter(stream, System.Text.Encoding.UTF8))
                {
                    writer.Write("""
                        <?xml version="1.0" encoding="utf-8"?>
                        <package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
                            <metadata>
                                <id>thirdparty-addon</id>
                                <version>1.5.0-alpha</version>
                                <authors>CommunityAuthor</authors>
                            </metadata>
                        </package>
                        """);
                }
            }

            var info = _inspector.Inspect(tempZip);

            Assert.True(info.IsGenHubBuild);
            Assert.Equal("1.5.0-alpha", info.Version);
            Assert.Equal(GenHubBuildConstants.ChannelTest, info.BuildChannel);
            Assert.True(GenHubBuildConstants.IsPrereleaseChannel(info.BuildChannel));
        }
        finally
        {
            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }
        }
    }

    /// <summary>
    /// Verifies that filename-only inspection recognizes widened prerelease tokens such as alpha, rc, and preview.
    /// </summary>
    /// <param name="fileName">The archive file name to test.</param>
    [Theory]
    [InlineData("GenHub-1.2.0-alpha-win.zip")]
    [InlineData("GenHub-1.2.0-rc1-win.zip")]
    [InlineData("GenHub-1.2.0-preview-win.zip")]
    public void Inspect_FileNameWithWidenedPrereleaseTokens_ResolvesToTestChannel(string fileName)
    {
        var tempZip = Path.Combine(Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(fileName)}_{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = System.IO.Compression.ZipFile.Open(tempZip, System.IO.Compression.ZipArchiveMode.Create))
            {
            }

            var info = _inspector.Inspect(tempZip);

            Assert.True(info.IsGenHubBuild);
            Assert.Equal(GenHubBuildConstants.ChannelTest, info.BuildChannel);
            Assert.True(GenHubBuildConstants.IsPrereleaseChannel(info.BuildChannel));
        }
        finally
        {
            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }
        }
    }

    /// <summary>
    /// Verifies that a tool package with a nuspec id like GenHub.MapBuilder or title containing GenHub
    /// but without a GenHub binary or exact package identity is not misclassified as a GenHub application build.
    /// </summary>
    [Fact]
    public void Inspect_ArchiveWithToolPackageNuspecId_IsNotClassifiedAsGenHubBuild()
    {
        var tempNupkg = Path.Combine(Path.GetTempPath(), $"tool_{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = System.IO.Compression.ZipFile.Open(tempNupkg, System.IO.Compression.ZipArchiveMode.Create))
            {
                var nuspecEntry = archive.CreateEntry("tool.nuspec");
                using (var stream = nuspecEntry.Open())
                using (var writer = new StreamWriter(stream, System.Text.Encoding.UTF8))
                {
                    writer.Write("""
                        <?xml version="1.0" encoding="utf-8"?>
                        <package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
                            <metadata>
                                <id>GenHub.MapBuilder</id>
                                <version>1.0.0</version>
                                <authors>Community Author</authors>
                                <title>GenHub Map Builder Tool</title>
                            </metadata>
                        </package>
                        """);
                }

                var toolExe = archive.CreateEntry("MapBuilder.exe");
                using (var stream = toolExe.Open())
                {
                    stream.WriteByte(0x4D);
                    stream.WriteByte(0x5A);
                }
            }

            var info = _inspector.Inspect(tempNupkg);

            Assert.False(info.IsGenHubBuild);
        }
        finally
        {
            if (File.Exists(tempNupkg))
            {
                File.Delete(tempNupkg);
            }
        }
    }

    /// <summary>
    /// Verifies that bundled helper binaries (e.g. vcredist_x64.exe) do not hijack classification over the primary application binary.
    /// </summary>
    [Fact]
    public void Inspect_ArchiveWithRenamedExeAndIgnoredHelperBinary_ChoosesMainExecutableNotHelper()
    {
        var tempNupkg = Path.Combine(Path.GetTempPath(), $"package_{Guid.NewGuid():N}.nupkg");
        try
        {
            using (var archive = System.IO.Compression.ZipFile.Open(tempNupkg, System.IO.Compression.ZipArchiveMode.Create))
            {
                var nuspecEntry = archive.CreateEntry("package.nuspec");
                using (var stream = nuspecEntry.Open())
                using (var writer = new StreamWriter(stream, System.Text.Encoding.UTF8))
                {
                    writer.Write("""
                        <?xml version="1.0" encoding="utf-8"?>
                        <package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
                            <metadata>
                                <id>GenHub</id>
                                <version>3.0.0</version>
                                <authors>CommunityForkAuthor</authors>
                            </metadata>
                        </package>
                        """);
                }

                // Helper appears first in archive
                var helperEntry = archive.CreateEntry("vcredist_x64.exe");
                using (var stream = helperEntry.Open())
                {
                    stream.WriteByte(0x4D);
                    stream.WriteByte(0x5A);
                }

                // Actual app executable
                var appEntry = archive.CreateEntry("CustomApp.exe");
                using (var stream = appEntry.Open())
                {
                    stream.WriteByte(0x4D);
                    stream.WriteByte(0x5A);
                }
            }

            var info = _inspector.Inspect(tempNupkg);

            Assert.True(info.IsGenHubBuild);
            Assert.Equal("CustomApp.exe", info.EntryPoint);
        }
        finally
        {
            if (File.Exists(tempNupkg))
            {
                File.Delete(tempNupkg);
            }
        }
    }

    /// <summary>
    /// Verifies that candidate binary selection prefers an executable matching the nuspec id
    /// over a bundled secondary binary that merely contains the generic GenHub substring.
    /// </summary>
    [Fact]
    public void Inspect_ArchiveWithForkIdAndBundledGenHubHelper_PrefersForkExecutableOverGenHubHelper()
    {
        var tempNupkg = Path.Combine(Path.GetTempPath(), $"package_{Guid.NewGuid():N}.nupkg");
        try
        {
            using (var archive = System.IO.Compression.ZipFile.Open(tempNupkg, System.IO.Compression.ZipArchiveMode.Create))
            {
                var nuspecEntry = archive.CreateEntry("package.nuspec");
                using (var stream = nuspecEntry.Open())
                using (var writer = new StreamWriter(stream, System.Text.Encoding.UTF8))
                {
                    writer.Write("""
                        <?xml version="1.0" encoding="utf-8"?>
                        <package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
                            <metadata>
                                <id>SuperFork</id>
                                <version>1.0.0</version>
                                <authors>CommunityForkAuthor</authors>
                                <title>GenHub (SuperFork)</title>
                            </metadata>
                        </package>
                        """);
                }

                // Generic GenHub helper appearing first
                var helperEntry = archive.CreateEntry("GenHubUpdater.exe");
                using (var stream = helperEntry.Open())
                {
                    stream.WriteByte(0x4D);
                    stream.WriteByte(0x5A);
                }

                // Fork executable matching nuspec id
                var appEntry = archive.CreateEntry("SuperFork.exe");
                using (var stream = appEntry.Open())
                {
                    stream.WriteByte(0x4D);
                    stream.WriteByte(0x5A);
                }
            }

            var info = _inspector.Inspect(tempNupkg);

            Assert.True(info.IsGenHubBuild);
            Assert.Equal("SuperFork.exe", info.EntryPoint);
        }
        finally
        {
            if (File.Exists(tempNupkg))
            {
                File.Delete(tempNupkg);
            }
        }
    }

    /// <summary>
    /// Verifies that an archive with a non-version digit continuation like GenHub-3DViewer.zip
    /// is not misclassified as a GenHub build.
    /// </summary>
    [Fact]
    public void Inspect_ArchiveWithDigitWordPrefix_IsNotClassifiedAsGenHubBuild()
    {
        var tempZip = Path.Combine(Path.GetTempPath(), $"GenHub-3DViewer_{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = System.IO.Compression.ZipFile.Open(tempZip, System.IO.Compression.ZipArchiveMode.Create))
            {
            }

            var info = _inspector.Inspect(tempZip);

            Assert.False(info.IsGenHubBuild);
        }
        finally
        {
            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }
        }
    }

    /// <summary>
    /// Verifies that a tool package with a tool nuspec id (e.g. GenHub.MapBuilder) and primary tool binary MapBuilder.exe
    /// bundling a secondary GenHub.dll dependency is not misclassified as a GenHub application build.
    /// </summary>
    [Fact]
    public void Inspect_ArchiveWithToolBinaryIdentity_IsNotClassifiedAsGenHubBuild()
    {
        var tempZip = Path.Combine(Path.GetTempPath(), $"ToolPackage_{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = System.IO.Compression.ZipFile.Open(tempZip, System.IO.Compression.ZipArchiveMode.Create))
            {
                var nuspecEntry = archive.CreateEntry("tool.nuspec");
                using (var stream = nuspecEntry.Open())
                using (var writer = new StreamWriter(stream, System.Text.Encoding.UTF8))
                {
                    writer.Write("""
                        <?xml version="1.0" encoding="utf-8"?>
                        <package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
                            <metadata>
                                <id>GenHub.MapBuilder</id>
                                <version>1.0.0</version>
                                <authors>Community Author</authors>
                                <title>GenHub Map Builder Tool</title>
                            </metadata>
                        </package>
                        """);
                }

                // Primary tool binary
                var entry = archive.CreateEntry("MapBuilder.exe");
                using (var stream = entry.Open())
                {
                    stream.WriteByte(0x4D);
                    stream.WriteByte(0x5A);
                }

                // Bundled secondary GenHub library
                var libEntry = archive.CreateEntry("GenHub.dll");
                using (var stream = libEntry.Open())
                {
                    stream.WriteByte(0x4D);
                    stream.WriteByte(0x5A);
                }
            }

            var info = _inspector.Inspect(tempZip);

            Assert.False(info.IsGenHubBuild);
        }
        finally
        {
            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }
        }
    }

    /// <summary>
    /// Verifies that an archive with a non-GenHub name containing only GenHub.dll
    /// without nuspec or PE metadata branding is not classified as a GenHub application build.
    /// </summary>
    [Fact]
    public void Inspect_ArchiveWithBundledGenHubDllOnlyAndNoBranding_IsNotClassifiedAsGenHubBuild()
    {
        var tempZip = Path.Combine(Path.GetTempPath(), $"ModPackage_{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = System.IO.Compression.ZipFile.Open(tempZip, System.IO.Compression.ZipArchiveMode.Create))
            {
                var libEntry = archive.CreateEntry("GenHub.dll");
                using (var stream = libEntry.Open())
                {
                    stream.WriteByte(0x4D);
                    stream.WriteByte(0x5A);
                }
            }

            var info = _inspector.Inspect(tempZip);

            Assert.False(info.IsGenHubBuild);
        }
        finally
        {
            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }
        }
    }
}
