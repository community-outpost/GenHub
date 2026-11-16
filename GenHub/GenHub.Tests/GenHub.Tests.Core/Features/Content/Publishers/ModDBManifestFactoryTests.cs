using AngleSharp;
using AngleSharp.Dom;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Parsers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.ContentResolvers;
using GenHub.Features.Content.Services.Parsers;
using GenHub.Features.Content.Services.Publishers;
using GenHub.Features.Manifest;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Publishers;

/// <summary>
/// Regression tests for ModDB download post-processing.
/// </summary>
public sealed class ModDBManifestFactoryTests : IDisposable
{
    private readonly string _stagingDirectory = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Verifies a ZIP without an extension is extracted into its payload files.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_ExtensionlessZip_ExtractsPayloadInsteadOfManifestingArchiveAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var archivePath = Path.Combine(_stagingDirectory, "moddb-download");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("Data/GenSpeed.ini").Open());
            await writer.WriteAsync("speed=fast");
        }

        var factory = CreateFactory();
        var original = new ContentManifest
        {
            Id = "1.0.moddb.moddingtool.genspeed",
            Name = "GenSpeed",
            ContentType = ContentType.ModdingTool,
        };

        // Act
        var manifestsResult = await factory.CreateManifestsFromExtractedContentAsync(original, _stagingDirectory);

        // Assert
        Assert.True(manifestsResult.Success);
        var manifest = Assert.Single(manifestsResult.Data!);
        var file = Assert.Single(manifest.Files);
        Assert.Equal(Path.Combine("Data", "GenSpeed.ini"), file.RelativePath);
        Assert.Equal(ContentSourceType.ExtractedPackage, file.SourceType);
        Assert.False(System.IO.File.Exists(archivePath));
        Assert.True(System.IO.File.Exists(Path.Combine(_stagingDirectory, "Data", "GenSpeed.ini")));
    }

    /// <summary>
    /// Verifies that a ModDB map archive is extracted and its payload is linked to the user map
    /// directory rather than the profile workspace.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_ExtensionlessMapArchive_UsesUserMapsDirectoryAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var archivePath = Path.Combine(_stagingDirectory, "lemuria-download");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("Lemuria/Lemuria.map").Open());
            await writer.WriteAsync("map payload");
        }

        var factory = CreateFactory();
        var original = new ContentManifest
        {
            Id = "1.0.moddb.map.lemuria",
            Name = "Lemuria",
            ContentType = ContentType.Map,
        };

        // Act
        var manifestResult = await factory.CreateManifestsFromExtractedContentAsync(original, _stagingDirectory);
        Assert.True(manifestResult.Success);
        var manifest = Assert.Single(manifestResult.Data!);

        // Assert
        var file = Assert.Single(manifest.Files);
        Assert.Equal(Path.Combine("Lemuria", "Lemuria.map"), file.RelativePath);
        Assert.Equal(ContentSourceType.ExtractedPackage, file.SourceType);
        Assert.Equal(ContentInstallTarget.UserMapsDirectory, file.InstallTarget);
    }

    /// <summary>
    /// Verifies that if the extracted content directory does not exist, CreateManifestsFromExtractedContentAsync
    /// returns the original manifest instead of an empty list.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_ExtractedDirectoryDoesNotExist_ReturnsOriginalManifestAsync()
    {
        // Arrange
        var factory = CreateFactory();
        var nonExistentPath = Path.Combine(_stagingDirectory, "does-not-exist");
        var original = new ContentManifest
        {
            Id = "1.0.moddb.mod.test",
            Name = "Test",
            ContentType = ContentType.Mod,
        };

        // Act
        var manifestsResult = await factory.CreateManifestsFromExtractedContentAsync(original, nonExistentPath);

        // Assert
        Assert.True(manifestsResult.Success);
        var manifest = Assert.Single(manifestsResult.Data!);
        Assert.Same(original, manifest);
    }

    /// <summary>
    /// Verifies that CreateManifestAsync stages remote files with RemoteDownload source type.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CreateManifestAsync_StagesFilesWithRemoteDownloadSourceTypeAsync()
    {
        // Arrange
        var factory = CreateFactory(CreateManifestBuilder);
        var mapDetails = new GenHub.Core.Models.ModDB.MapDetails(
            Name: "Test Map",
            Description: "Test Description",
            Author: "Tester",
            PreviewImage: "https://example.com/icon.png",
            Screenshots: [],
            FileSize: 1024,
            DownloadCount: 10,
            SubmissionDate: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            DownloadUrl: "https://www.moddb.com/downloads/start/12345",
            TargetGame: GameType.ZeroHour,
            ContentType: ContentType.Map,
            FileType: ".zip",
            AdditionalFiles: null);

        // Act
        var manifest = await factory.CreateManifestAsync(mapDetails, "https://www.moddb.com/addons/test-map");

        // Assert
        var file = Assert.Single(manifest.Files);
        Assert.Equal(ContentSourceType.RemoteDownload, file.SourceType);
        Assert.Equal("https://www.moddb.com/downloads/start/12345", file.DownloadUrl);
    }

    /// <summary>
    /// Verifies a ModDB addon row categorised as a single-player map stays a map through the
    /// resolver and archive factory, ending at the user maps directory.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ResolveAndExtractAsync_AddonSingleplayerMap_UsesMapManifestAndUserMapsDirectoryAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var archivePath = Path.Combine(_stagingDirectory, "moddb-download");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("Lemuria/Lemuria.map").Open());
            await writer.WriteAsync("map payload");
        }

        var factory = CreateFactory(CreateManifestBuilder);
        var parser = new ModDBPageParser(
            new Mock<IPlaywrightService>(MockBehavior.Strict).Object,
            new Mock<ILogger<ModDBPageParser>>().Object);
        var resolver = new ModDBResolver(
            factory,
            parser,
            new Mock<ILogger<ModDBResolver>>().Object);
        var selectedUrl = "https://www.moddb.com/addons/start/302328";
        var result = new ContentSearchResult
        {
            Id = "catalog-lemuria",
            Name = "Lemuria 2026",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://www.moddb.com/games/cc-generals-zero-hour/addons/lemuria-2026-fixes",
            SelectedDownloadUrl = selectedUrl,
            ResolverId = "ModDB",
            RequiresResolution = true,
            ParsedPageData = new ParsedWebPage(
                new Uri("https://www.moddb.com/games/cc-generals-zero-hour/addons/lemuria-2026-fixes"),
                new GlobalContext("Lemuria 2026", "BagaturKhan", new DateTime(2026, 1, 2)),
                [
                    new DownloadableFile(
                        Name: "Lemuria_2026_Fixes.rar",
                        Category: "Singleplayer Map",
                        DownloadUrl: selectedUrl,
                        FileSectionType: FileSectionType.Addons),
                ],
                PageType.Detail),
        };

        // Act
        var resolution = await resolver.ResolveAsync(result);
        Assert.True(resolution.Success, resolution.FirstError);
        var manifest = Assert.IsType<ContentManifest>(resolution.Data);
        var extractedResult = await factory.CreateManifestsFromExtractedContentAsync(manifest, _stagingDirectory);
        Assert.True(extractedResult.Success, extractedResult.FirstError);
        var extractedManifest = Assert.Single(extractedResult.Data!);

        // Assert
        Assert.Equal(ContentType.Map, manifest.ContentType);
        Assert.Equal(ContentType.Map, extractedManifest.ContentType);
        Assert.All(extractedManifest.Files, file => Assert.Equal(ContentInstallTarget.UserMapsDirectory, file.InstallTarget));
    }

    /// <summary>
    /// Verifies that resolving an addon with a shallow listing URL invokes ParseFileDetailAsync
    /// to obtain the direct /start/ download URL and proper file extension.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ModDBResolver_ResolvesAddonWithShallowUrl_FetchesDetailAndCreatesManifestWithStartUrlAsync()
    {
        // Arrange
        var playwright = new Mock<IPlaywrightService>();
        var detailHtml = """
            <html>
            <head><title>Lost Warlord - by Lebi addon - ModDB</title></head>
            <body>
                <div id="profile">
                    <div class="row clear"><span class="label">Filename</span><span class="summary">Lost_Warlord.rar</span></div>
                    <div class="row clear"><span class="label">Category</span><span class="summary">Singleplayer Map</span></div>
                    <div class="row clear"><span class="label">Size</span><span class="summary">261.74kb (268,025 bytes)</span></div>
                    <div class="row clear"><span class="label">MD5 Hash</span><span class="summary">5d0649e2fa69d4ec8697b46a5aa33c1f</span></div>
                    <div class="row clear"><span class="label">Uploader</span><span class="summary">Lebi182</span></div>
                </div>
                <div class="row clear">
                    <a class="button buttonlarge" href="/addons/start/305556">Download Now</a>
                </div>
            </body>
            </html>
            """;
        playwright
            .Setup(p => p.FetchAndParsePersistentAsync(
                ModDBConstants.BrowserProfileName,
                "https://www.moddb.com/mods/cc-generals-zero-hour-enhanced/addons/lost-warlord",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(await CreateDocumentAsync(detailHtml));

        var factory = CreateFactory(CreateManifestBuilder);
        var parser = new ModDBPageParser(
            playwright.Object,
            new Mock<ILogger<ModDBPageParser>>().Object);
        var resolver = new ModDBResolver(
            factory,
            parser,
            new Mock<ILogger<ModDBResolver>>().Object);

        var shallowAddonUrl = "https://www.moddb.com/mods/cc-generals-zero-hour-enhanced/addons/lost-warlord";
        var result = new ContentSearchResult
        {
            Id = "catalog-parent",
            Name = "Lost Warlord - by Lebi",
            ProviderName = "ModDB",
            ContentType = ContentType.Map,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://www.moddb.com/mods/cc-generals-zero-hour-enhanced",
            SelectedDownloadUrl = shallowAddonUrl,
            ResolverId = "ModDB",
            RequiresResolution = true,
            ParsedPageData = new ParsedWebPage(
                new Uri("https://www.moddb.com/mods/cc-generals-zero-hour-enhanced"),
                new GlobalContext("C&C Generals Zero Hour: Enhanced", "Acoustic Alpha", new DateTime(2024, 3, 28)),
                [
                    new DownloadableFile(
                        Name: "ZeroHour Enhanced V1.0 Patch",
                        DownloadUrl: "https://www.moddb.com/downloads/start/313875",
                        FileSectionType: FileSectionType.Downloads),
                    new DownloadableFile(
                        Name: "Lost Warlord - by Lebi",
                        Category: "Singleplayer Map",
                        DownloadUrl: shallowAddonUrl,
                        DetailsUrl: shallowAddonUrl,
                        FileSectionType: FileSectionType.Addons),
                ],
                PageType.Detail),
        };

        // Act
        var resolution = await resolver.ResolveAsync(result);

        // Assert
        Assert.True(resolution.Success, resolution.FirstError);
        var manifest = Assert.IsType<ContentManifest>(resolution.Data);
        Assert.Equal(ContentType.Map, manifest.ContentType);
        Assert.Equal("Lost Warlord - by Lebi", manifest.Name);
        var remoteFile = Assert.Single(manifest.Files);
        Assert.Equal("https://www.moddb.com/addons/start/305556", remoteFile.DownloadUrl);
        Assert.EndsWith(".rar", remoteFile.RelativePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that resolving an addon whose SelectedDownloadUrl is already a direct /start/ URL
    /// does NOT invoke ParseFileDetailAsync and uses the direct URL directly.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ModDBResolver_ResolvesAddonWithDirectStartUrl_SkipsParseFileDetailAndCreatesManifestWithDirectUrlAsync()
    {
        // Arrange
        var playwright = new Mock<IPlaywrightService>(MockBehavior.Strict);
        var factory = CreateFactory(CreateManifestBuilder);
        var parser = new ModDBPageParser(
            playwright.Object,
            new Mock<ILogger<ModDBPageParser>>().Object);
        var resolver = new ModDBResolver(
            factory,
            parser,
            new Mock<ILogger<ModDBResolver>>().Object);

        const string directAddonUrl = "https://www.moddb.com/addons/start/305556";
        var result = new ContentSearchResult
        {
            Id = "catalog-parent",
            Name = "Lost Warlord - by Lebi",
            ProviderName = "ModDB",
            ContentType = ContentType.Map,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://www.moddb.com/mods/cc-generals-zero-hour-enhanced",
            SelectedDownloadUrl = directAddonUrl,
            ResolverId = "ModDB",
            RequiresResolution = true,
            ParsedPageData = new ParsedWebPage(
                new Uri("https://www.moddb.com/mods/cc-generals-zero-hour-enhanced"),
                new GlobalContext("C&C Generals Zero Hour: Enhanced", "Acoustic Alpha", new DateTime(2024, 3, 28)),
                [
                    new DownloadableFile(
                        Name: "Lost Warlord - by Lebi",
                        Category: "Singleplayer Map",
                        DownloadUrl: "https://www.moddb.com/mods/cc-generals-zero-hour-enhanced/addons/lost-warlord",
                        DetailsUrl: "https://www.moddb.com/mods/cc-generals-zero-hour-enhanced/addons/lost-warlord",
                        FileSectionType: FileSectionType.Addons),
                ],
                PageType.Detail),
        };

        // Act
        var resolution = await resolver.ResolveAsync(result);

        // Assert - resolver succeeded without invoking playwright to scrape detail page
        Assert.True(resolution.Success, resolution.FirstError);
        var manifest = Assert.IsType<ContentManifest>(resolution.Data);
        Assert.Equal(ContentType.Map, manifest.ContentType);
        Assert.Equal("Lost Warlord - by Lebi", manifest.Name);
        var remoteFile = Assert.Single(manifest.Files);
        Assert.Equal(directAddonUrl, remoteFile.DownloadUrl);
    }

    /// <summary>
    /// Verifies that resolving a ModDB item with explicit content type metadata preserves the
    /// user-selected content type instead of overriding it with the file category or section default,
    /// and that standalone content types do not add game installation dependencies.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ModDBResolver_ExplicitContentTypeMetadata_PreservesUserSelectedContentTypeAsync()
    {
        // Arrange
        var playwright = new Mock<IPlaywrightService>(MockBehavior.Strict);
        var factory = CreateFactory(CreateManifestBuilder);
        var parser = new ModDBPageParser(
            playwright.Object,
            new Mock<ILogger<ModDBPageParser>>().Object);
        var resolver = new ModDBResolver(
            factory,
            parser,
            new Mock<ILogger<ModDBResolver>>().Object);

        const string directAddonUrl = "https://www.moddb.com/addons/start/305556";
        var result = new ContentSearchResult
        {
            Id = "catalog-parent",
            Name = "WB World Builder Tool",
            ProviderName = "ModDB",
            ContentType = ContentType.ModdingTool,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://www.moddb.com/mods/cc-generals-zero-hour-enhanced",
            SelectedDownloadUrl = directAddonUrl,
            ResolverId = "ModDB",
            RequiresResolution = true,
            ResolverMetadata =
            {
                [ContentConstants.ExplicitContentTypeMetadataKey] = ContentConstants.ExplicitContentTypeEnabledValue,
            },
            ParsedPageData = new ParsedWebPage(
                new Uri("https://www.moddb.com/mods/cc-generals-zero-hour-enhanced"),
                new GlobalContext("C&C Generals Zero Hour: Enhanced", "Acoustic Alpha", new DateTime(2024, 3, 28)),
                [
                    new DownloadableFile(
                        Name: "WB_Tool.rar",
                        Category: "Singleplayer Map",
                        DownloadUrl: "https://www.moddb.com/mods/cc-generals-zero-hour-enhanced/addons/wb-tool",
                        DetailsUrl: "https://www.moddb.com/mods/cc-generals-zero-hour-enhanced/addons/wb-tool",
                        FileSectionType: FileSectionType.Addons),
                ],
                PageType.Detail),
        };

        // Act
        var resolution = await resolver.ResolveAsync(result);

        // Assert
        Assert.True(resolution.Success, resolution.FirstError);
        var manifest = Assert.IsType<ContentManifest>(resolution.Data);
        Assert.Equal(ContentType.ModdingTool, manifest.ContentType);
        Assert.DoesNotContain(manifest.Dependencies, d => d.DependencyType == ContentType.GameInstallation);
    }

    /// <summary>
    /// Guards against creating a profile manifest for an opaque ModDB transport artifact.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_ExtensionlessNonArchive_RejectsArtifactAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        await System.IO.File.WriteAllTextAsync(Path.Combine(_stagingDirectory, "transport-artifact"), "not an archive");
        var factory = CreateFactory();
        var original = new ContentManifest
        {
            Id = "1.0.moddb.addon.opaque",
            Name = "Opaque artifact",
            ContentType = ContentType.Addon,
            Files =
            [
                new ManifestFile
                {
                    RelativePath = "transport-artifact",
                },
            ],
        };

        // Act
        var result = await factory.CreateManifestsFromExtractedContentAsync(original, _stagingDirectory);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("extensionless non-archive", result.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that a ModDB mod archive with multi-level nested folders (e.g. C&amp;C Generals Undone v1.0)
    /// has its directory structure normalized so game assets are located at the root in the final manifest.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_NestedModArchive_FlattensPayloadToRootAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var zipPath = Path.Combine(_stagingDirectory, "GeneralsUndone.zip");
        {
            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            {
                using var writer1 = new StreamWriter(archive.CreateEntry("C&C Generals Undone v1.0/C&C Generals Undone v1.0/Readme.txt").Open());
                await writer1.WriteAsync("Readme");
            }

            {
                using var writer2 = new StreamWriter(archive.CreateEntry("C&C Generals Undone v1.0/C&C Generals Undone v1.0/Art/Textures/tex.tga").Open());
                await writer2.WriteAsync("Texture");
            }

            {
                using var writer3 = new StreamWriter(archive.CreateEntry("C&C Generals Undone v1.0/C&C Generals Undone v1.0/Data/INI/GameData.ini").Open());
                await writer3.WriteAsync("GameData");
            }

            {
                using var writer4 = new StreamWriter(archive.CreateEntry("C&C Generals Undone v1.0/C&C Generals Undone v1.0/Window/MainMenu.wnd").Open());
                await writer4.WriteAsync("Window");
            }
        }

        var factory = CreateFactory();
        var original = new ContentManifest
        {
            Id = "1.0.moddb.mod.generalsundone",
            Name = "C&C Generals Undone",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Files =
            [
                new ManifestFile
                {
                    RelativePath = "GeneralsUndone.zip",
                },
            ],
        };

        // Act
        var manifestsResult = await factory.CreateManifestsFromExtractedContentAsync(original, _stagingDirectory);

        // Assert
        Assert.True(manifestsResult.Success);
        var manifest = Assert.Single(manifestsResult.Data!);
        Assert.Contains(manifest.Files, f => f.RelativePath == "Readme.txt");
        Assert.Contains(manifest.Files, f => f.RelativePath == Path.Combine("Art", "Textures", "tex.tga"));
        Assert.Contains(manifest.Files, f => f.RelativePath == Path.Combine("Data", "INI", "GameData.ini"));
        Assert.Contains(manifest.Files, f => f.RelativePath == Path.Combine("Window", "MainMenu.wnd"));
        Assert.DoesNotContain(manifest.Files, f => f.RelativePath.Contains("C&C Generals Undone v1.0"));
        Assert.All(manifest.Files, f => Assert.Equal(ContentInstallTarget.Workspace, f.InstallTarget));
    }

    /// <summary>
    /// Verifies that if a Mod download results only in an unextracted setup/installer executable,
    /// CreateManifestsFromExtractedContentAsync returns a failure result.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_WithUnextractedInstallerExe_ReturnsFailureAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var setupExePath = Path.Combine(_stagingDirectory, "Contra X BETA 2 (Setup).exe");
        await System.IO.File.WriteAllTextAsync(setupExePath, "dummy installer binary");

        var originalManifest = new ContentManifest
        {
            SchemaVersion = "1.0.0",
            Id = ManifestId.Create("1.1.moddb.mod.contra-x-beta-2"),
            Name = "Contra X BETA 2",
            Version = "1.0",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { Name = "Contra Mod Team" },
            Metadata = new ContentMetadata
            {
                Description = "Contra X BETA 2",
            },
        };

        var factory = CreateFactory();

        // Act
        var result = await factory.CreateManifestsFromExtractedContentAsync(originalManifest, _stagingDirectory, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("unextracted installer executable", result.FirstError);
        Assert.Contains("Contra X BETA 2 (Setup).exe", result.FirstError);
    }

    /// <summary>
    /// Deletes the test staging directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_stagingDirectory))
        {
            Directory.Delete(_stagingDirectory, recursive: true);
        }
    }

    private static async Task<IDocument> CreateDocumentAsync(string html)
    {
        var context = BrowsingContext.New(Configuration.Default);
        return await context.OpenAsync(req => req.Content(html));
    }

    private ModDBManifestFactory CreateFactory(Func<IContentManifestBuilder>? builderFactory = null)
    {
        var hashProvider = new Mock<IFileHashProvider>();
        hashProvider
            .Setup(provider => provider.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("0123456789abcdef0123456789abcdef");

        var payloadProcessor = new GenHub.Features.Content.Services.Common.ArchivePayloadProcessor(
            new Mock<ILogger<GenHub.Features.Content.Services.Common.ArchivePayloadProcessor>>().Object);

        return new ModDBManifestFactory(
            builderFactory ?? (() => new Mock<IContentManifestBuilder>().Object),
            new Mock<IProviderDefinitionLoader>().Object,
            hashProvider.Object,
            payloadProcessor,
            new Mock<ILogger<ModDBManifestFactory>>().Object);
    }

    private IContentManifestBuilder CreateManifestBuilder()
    {
        var manifestIdService = new Mock<IManifestIdService>();
        manifestIdService
            .Setup(service => service.ValidateAndCreateManifestId(It.IsAny<string>()))
            .Returns((string id) => OperationResult<ManifestId>.CreateSuccess(ManifestId.Create(id)));
        manifestIdService
            .Setup(service => service.GeneratePublisherContentId(
                It.IsAny<string>(),
                It.IsAny<ContentType>(),
                It.IsAny<string>(),
                It.IsAny<int>()))
            .Returns((string publisherId, ContentType contentType, string contentName, int version) =>
                OperationResult<ManifestId>.CreateSuccess(
                    ManifestId.Create(ManifestIdGenerator.GeneratePublisherContentId(
                        publisherId,
                        contentType,
                        contentName,
                        version))));

        return new ContentManifestBuilder(
            new Mock<ILogger<ContentManifestBuilder>>().Object,
            new Mock<IFileHashProvider>().Object,
            manifestIdService.Object,
            new Mock<IDownloadService>().Object,
            new Mock<IConfigurationProviderService>().Object);
    }
}
