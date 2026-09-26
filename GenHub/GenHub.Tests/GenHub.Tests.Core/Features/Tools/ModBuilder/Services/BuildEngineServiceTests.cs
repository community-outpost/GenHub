using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.ModBuilder;
using GenHub.Features.Tools.ModBuilder.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.Services;

/// <summary>
/// Unit tests for <see cref="BuildEngineService"/>.
/// </summary>
public sealed class BuildEngineServiceTests : IDisposable
{
    private readonly Mock<IBuildCacheService> _mockCacheService;
    private readonly Mock<IFileConversionService> _mockFileConversionService;
    private readonly Mock<IMd5HashProvider> _mockHashProvider;
    private readonly Mock<IConfigurationLoaderService> _mockConfigurationLoaderService;
    private readonly Mock<IArchiveService> _mockArchiveService;
    private readonly Mock<ILocalContentService> _mockLocalContentService;
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
    private readonly Mock<ILogger<BuildEngineService>> _mockLogger;
    private readonly BuildEngineService _service;
    private readonly string _tempDirectory;

    public BuildEngineServiceTests()
    {
        _mockCacheService = new Mock<IBuildCacheService>();
        _mockFileConversionService = new Mock<IFileConversionService>();
        _mockHashProvider = new Mock<IMd5HashProvider>();
        _mockConfigurationLoaderService = new Mock<IConfigurationLoaderService>();
        _mockArchiveService = new Mock<IArchiveService>();
        _mockLocalContentService = new Mock<ILocalContentService>();
        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider
            .Setup(x => x.GetService(typeof(ILocalContentService)))
            .Returns(_mockLocalContentService.Object);
        mockScope.Setup(x => x.ServiceProvider).Returns(mockServiceProvider.Object);
        _mockScopeFactory = new Mock<IServiceScopeFactory>();
        _mockScopeFactory.Setup(x => x.CreateScope()).Returns(mockScope.Object);
        _mockLogger = new Mock<ILogger<BuildEngineService>>();

        _mockLocalContentService.Setup(x => x.CreateLocalContentManifestAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<GenHub.Core.Models.Enums.ContentType>(),
                It.IsAny<GameType>(),
                It.IsAny<string?>(),
                It.IsAny<IProgress<ContentStorageProgress>?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<GenHub.Core.Models.Manifest.ContentManifest>.CreateSuccess(
                new GenHub.Core.Models.Manifest.ContentManifest
                {
                    Id = GenHub.Core.Models.Manifest.ManifestId.Create("1.0.local.mod.manifestproject"),
                    Name = "ManifestProject",
                    TargetGame = GameType.Generals,
                    ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
                }));

        _mockHashProvider.Setup(x => x.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("default_hash");
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);

        _mockConfigurationLoaderService.Setup(x => x.ResolveWildcardsAsync(It.IsAny<BuildConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BuildConfiguration config, CancellationToken ct) => config);

        _mockArchiveService.Setup(x => x.CreateBigArchiveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, IProgress<double>?, CancellationToken>((_, target, _, _) =>
            {
                var dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                if (!File.Exists(target))
                {
                    File.WriteAllText(target, "dummy big content");
                }
            })
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateSuccess(true));

        _mockArchiveService.Setup(x => x.CreateBigArchiveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string?, IProgress<double>?, CancellationToken>((_, target, _, _, _) =>
            {
                var dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                if (!File.Exists(target))
                {
                    File.WriteAllText(target, "dummy big content");
                }
            })
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateSuccess(true));

        _mockArchiveService.Setup(x => x.CreateZipArchiveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<System.IO.Compression.CompressionLevel>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateSuccess(true));

        _service = new BuildEngineService(
            _mockCacheService.Object,
            _mockFileConversionService.Object,
            _mockHashProvider.Object,
            _mockConfigurationLoaderService.Object,
            _mockArchiveService.Object,
            _mockScopeFactory.Object,
            _mockLogger.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Constructor_WithValidDependencies_DoesNotThrow()
    {
        // Act
        var service = new BuildEngineService(
            _mockCacheService.Object,
            _mockFileConversionService.Object,
            _mockHashProvider.Object,
            _mockConfigurationLoaderService.Object,
            _mockArchiveService.Object,
            _mockScopeFactory.Object,
            _mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithValidProject_ReturnsSuccess()
    {
        // Arrange
        var project = new ModBuilderProject
        {
            Name = "TestProject",
            Directories = new ProjectDirectories
            {
                GameFilesEdited = _tempDirectory,
                Build = Path.Combine(_tempDirectory, "output")
            },
            BundleConfigs = new List<string>()
        };

        var configuration = new BuildConfiguration
        {
            Items = new List<BundleItem>(),
            Packs = new List<BundlePack>()
        };

        var selectedPacks = new List<string>();

        // Act
        var result = await _service.ExecuteBuildAsync(project, configuration, selectedPacks, BuildStep.Build);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue(result.FirstError);
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithNullProject_ThrowsException()
    {
        // Arrange
        ModBuilderProject? project = null;
        var configuration = new BuildConfiguration();
        var selectedPacks = new List<string>();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _service.ExecuteBuildAsync(project!, configuration, selectedPacks, BuildStep.Build));
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithProgress_ReportsProgress()
    {
        // Arrange
        var project = new ModBuilderProject
        {
            Name = "TestProject",
            Directories = new ProjectDirectories
            {
                GameFilesEdited = _tempDirectory,
                Build = Path.Combine(_tempDirectory, "output")
            },
            BundleConfigs = new List<string>()
        };

        var configuration = new BuildConfiguration
        {
            Items = new List<BundleItem>(),
            Packs = new List<BundlePack>()
        };

        var selectedPacks = new List<string>();
        var progressMock = new Mock<IProgress<BuildProgress>>();

        // Act
        var result = await _service.ExecuteBuildAsync(project, configuration, selectedPacks, BuildStep.Build, progressMock.Object);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        progressMock.Verify(p => p.Report(It.IsAny<BuildProgress>()), Times.AtLeastOnce());
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var project = new ModBuilderProject
        {
            Name = "TestProject",
            Directories = new ProjectDirectories
            {
                GameFilesEdited = _tempDirectory,
                Build = Path.Combine(_tempDirectory, "output")
            },
            BundleConfigs = new List<string>()
        };

        var configuration = new BuildConfiguration
        {
            Items = new List<BundleItem>(),
            Packs = new List<BundlePack>()
        };

        var selectedPacks = new List<string>();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await _service.ExecuteBuildAsync(project, configuration, selectedPacks, BuildStep.Build, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task CanAbortAsync_WhenNotRunning_ReturnsFalse()
    {
        // Act
        var result = await _service.CanAbortAsync();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task AbortAsync_WhenNotRunning_DoesNotThrow()
    {
        // Act
        var act = async () => await _service.AbortAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void InvalidateBuildStructureCache_ClearsCache()
    {
        // Act
        var act = () => _service.InvalidateBuildStructureCache();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithBundleItems_ProcessesItems()
    {
        // Arrange
        var sourceFile = Path.Combine(_tempDirectory, "source.txt");
        await File.WriteAllTextAsync(sourceFile, "content");

        var project = new ModBuilderProject
        {
            Name = "TestProject",
            Directories = new ProjectDirectories
            {
                GameFilesEdited = _tempDirectory,
                Build = Path.Combine(_tempDirectory, "output")
            },
            BundleConfigs = new List<string>()
        };

        var configuration = new BuildConfiguration
        {
            Items = new List<BundleItem>
            {
                new()
                {
                    Name = "TestItem",
                    Files = new List<BundleFile>
                    {
                        new()
                        {
                            AbsSourceParent = _tempDirectory,
                            AbsSourceFile = sourceFile,
                            RelTargetFile = "output.txt"
                        }
                    }
                }
            },
            Packs = new List<BundlePack>
            {
                new() { Name = "TestPack", ItemNames = new List<string> { "TestItem" } }
            }
        };

        var selectedPacks = new List<string> { "TestPack" };

        _mockHashProvider.Setup(x => x.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("hash123");

        _mockCacheService.Setup(x => x.DetermineFileStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .Returns(BuildFileStatus.Added);

        _mockFileConversionService.Setup(x => x.ConvertFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConversionOperationResult.CreateSuccess());

        // Act
        var result = await _service.ExecuteBuildAsync(project, configuration, selectedPacks, BuildStep.Build);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        result.FilesProcessed.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithUnchangedFiles_SkipsFiles()
    {
        // Arrange
        var sourceFile = Path.Combine(_tempDirectory, "source.txt");
        await File.WriteAllTextAsync(sourceFile, "content");

        var project = new ModBuilderProject
        {
            Name = "TestProject",
            Directories = new ProjectDirectories
            {
                GameFilesEdited = _tempDirectory,
                Build = Path.Combine(_tempDirectory, "output")
            },
            BundleConfigs = new List<string>()
        };

        var configuration = new BuildConfiguration
        {
            Items = new List<BundleItem>
            {
                new()
                {
                    Name = "TestItem",
                    Files = new List<BundleFile>
                    {
                        new()
                        {
                            AbsSourceParent = _tempDirectory,
                            AbsSourceFile = sourceFile,
                            RelTargetFile = "output.txt"
                        }
                    }
                }
            },
            Packs = new List<BundlePack>
            {
                new() { Name = "TestPack", ItemNames = new List<string> { "TestItem" } }
            }
        };

        var selectedPacks = new List<string> { "TestPack" };

        _mockHashProvider.Setup(x => x.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("hash123");

        _mockCacheService.Setup(x => x.DetermineFileStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .Returns(BuildFileStatus.Unchanged);

        var expectedOutputDir = Path.Combine(_tempDirectory, "output", ModBuilderConstants.RawBundleItemsSubdir);
        Directory.CreateDirectory(expectedOutputDir);
        await File.WriteAllTextAsync(Path.Combine(expectedOutputDir, "output.txt"), "content");

        // Act
        var result = await _service.ExecuteBuildAsync(project, configuration, selectedPacks, BuildStep.Build);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        result.FilesSkipped.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithFailedConversion_IncrementsFailedCount()
    {
        // Arrange
        var sourceFile = Path.Combine(_tempDirectory, "source.png");
        await File.WriteAllTextAsync(sourceFile, "content");

        var project = new ModBuilderProject
        {
            Name = "TestProject",
            Directories = new ProjectDirectories
            {
                GameFilesEdited = _tempDirectory,
                Build = Path.Combine(_tempDirectory, "output")
            },
            BundleConfigs = new List<string>()
        };

        var configuration = new BuildConfiguration
        {
            Items = new List<BundleItem>
            {
                new()
                {
                    Name = "TestItem",
                    Files = new List<BundleFile>
                    {
                        new()
                        {
                            AbsSourceParent = _tempDirectory,
                            AbsSourceFile = sourceFile,
                            RelTargetFile = "output.png"
                        }
                    }
                }
            },
            Packs = new List<BundlePack>
            {
                new() { Name = "TestPack", ItemNames = new List<string> { "TestItem" } }
            }
        };

        var selectedPacks = new List<string> { "TestPack" };

        _mockHashProvider.Setup(x => x.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("hash123");

        _mockCacheService.Setup(x => x.DetermineFileStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .Returns(BuildFileStatus.Added);

        _mockFileConversionService.Setup(x => x.ConvertFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConversionOperationResult.CreateFailure("Conversion failed"));

        // Act
        var result = await _service.ExecuteBuildAsync(project, configuration, selectedPacks, BuildStep.Build);

        // Assert
        result.FilesFailed.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithEmptyConfiguration_ReturnsSuccess()
    {
        // Arrange
        var project = new ModBuilderProject
        {
            Name = "TestProject",
            Directories = new ProjectDirectories
            {
                GameFilesEdited = _tempDirectory,
                Build = Path.Combine(_tempDirectory, "output")
            },
            BundleConfigs = new List<string>()
        };

        var configuration = new BuildConfiguration
        {
            Items = new List<BundleItem>(),
            Packs = new List<BundlePack>()
        };

        var selectedPacks = new List<string>();

        // Act
        var result = await _service.ExecuteBuildAsync(project, configuration, selectedPacks, BuildStep.Build);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        result.FilesProcessed.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithMultiplePacks_ProcessesAllPacks()
    {
        // Arrange
        var sourceFile1 = Path.Combine(_tempDirectory, "source1.txt");
        var sourceFile2 = Path.Combine(_tempDirectory, "source2.txt");
        await File.WriteAllTextAsync(sourceFile1, "content1");
        await File.WriteAllTextAsync(sourceFile2, "content2");

        var project = new ModBuilderProject
        {
            Name = "TestProject",
            Directories = new ProjectDirectories
            {
                GameFilesEdited = _tempDirectory,
                Build = Path.Combine(_tempDirectory, "output")
            },
            BundleConfigs = new List<string>()
        };

        var configuration = new BuildConfiguration
        {
            Items = new List<BundleItem>
            {
                new()
                {
                    Name = "Item1",
                    Files = new List<BundleFile>
                    {
                        new()
                        {
                            AbsSourceParent = _tempDirectory,
                            AbsSourceFile = sourceFile1,
                            RelTargetFile = "output1.txt"
                        }
                    }
                },
                new()
                {
                    Name = "Item2",
                    Files = new List<BundleFile>
                    {
                        new()
                        {
                            AbsSourceParent = _tempDirectory,
                            AbsSourceFile = sourceFile2,
                            RelTargetFile = "output2.txt"
                        }
                    }
                }
            },
            Packs = new List<BundlePack>
            {
                new() { Name = "Pack1", ItemNames = new List<string> { "Item1" } },
                new() { Name = "Pack2", ItemNames = new List<string> { "Item2" } }
            }
        };

        var selectedPacks = new List<string> { "Pack1", "Pack2" };

        _mockHashProvider.Setup(x => x.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("hash123");

        _mockCacheService.Setup(x => x.DetermineFileStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .Returns(BuildFileStatus.Added);

        _mockFileConversionService.Setup(x => x.ConvertFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConversionOperationResult.CreateSuccess());

        // Act
        var result = await _service.ExecuteBuildAsync(project, configuration, selectedPacks, BuildStep.Build);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        result.FilesProcessed.Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithGeneralsGamePatch2Structure_BuildsAllItemsAndPacks()
    {
        // Arrange
        var patchProjectDir = Path.Combine(_tempDirectory, "GeneralsGamePatch2");
        var editedDir = Path.Combine(patchProjectDir, "GameFilesEdited");
        var buildDir = Path.Combine(patchProjectDir, ".Build");
        var releaseDir = Path.Combine(patchProjectDir, ".Release");

        Directory.CreateDirectory(Path.Combine(editedDir, "Data", "INI"));
        Directory.CreateDirectory(Path.Combine(editedDir, "Art", "Textures"));
        Directory.CreateDirectory(Path.Combine(editedDir, "Data", "Audio"));
        Directory.CreateDirectory(Path.Combine(editedDir, "Data", "Scripts"));

        var iniFile = Path.Combine(editedDir, "Data", "INI", "GameData.ini");
        var texFile = Path.Combine(editedDir, "Art", "Textures", "CrusaderTank.tga");
        var audFile = Path.Combine(editedDir, "Data", "Audio", "TankMove.wav");
        var scrFile = Path.Combine(editedDir, "Data", "Scripts", "CommunityFixes.txt");

        await File.WriteAllTextAsync(iniFile, "GameData content");
        await File.WriteAllTextAsync(texFile, "TGA content");
        await File.WriteAllTextAsync(audFile, "WAV content");
        await File.WriteAllTextAsync(scrFile, "TXT content");

        var project = new ModBuilderProject
        {
            Name = "GeneralsGamePatch2",
            ProjectDir = patchProjectDir,
            Directories = new ProjectDirectories
            {
                GameFilesEdited = editedDir,
                Build = buildDir,
                Release = releaseDir,
            },
            BundleConfigs = new List<string>()
        };

        var configuration = new BuildConfiguration
        {
            Folders = new FolderConfiguration
            {
                AbsBuildDir = buildDir,
                AbsReleaseDir = releaseDir,
            },
            Items = new List<BundleItem>
            {
                new()
                {
                    Name = "PatchINI",
                    IsBig = true,
                    Files = new List<BundleFile>
                    {
                        new() { AbsSourceParent = patchProjectDir, AbsSourceFile = iniFile, RelTargetFile = "Data/INI/GameData.ini" }
                    }
                },
                new()
                {
                    Name = "PatchTextures",
                    IsBig = true,
                    Files = new List<BundleFile>
                    {
                        new() { AbsSourceParent = patchProjectDir, AbsSourceFile = texFile, RelTargetFile = "Art/Textures/CrusaderTank.tga" }
                    }
                },
                new()
                {
                    Name = "PatchAudio",
                    IsBig = true,
                    Files = new List<BundleFile>
                    {
                        new() { AbsSourceParent = patchProjectDir, AbsSourceFile = audFile, RelTargetFile = "Data/Audio/TankMove.wav" }
                    }
                },
                new()
                {
                    Name = "PatchScripts",
                    IsBig = true,
                    Files = new List<BundleFile>
                    {
                        new() { AbsSourceParent = patchProjectDir, AbsSourceFile = scrFile, RelTargetFile = "Data/Scripts/CommunityFixes.txt" }
                    }
                }
            },
            Packs = new List<BundlePack>
            {
                new()
                {
                    Name = "GeneralsGamePatch2",
                    AllowBuild = true,
                    AllowInstall = true,
                    ItemNames = new List<string> { "PatchINI", "PatchTextures", "PatchAudio", "PatchScripts" },
                },
                new()
                {
                    Name = "PatchINIOnly",
                    AllowBuild = true,
                    AllowInstall = true,
                    ItemNames = new List<string> { "PatchINI" },
                }
            }
        };

        _mockHashProvider.Setup(x => x.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("patchhash123");

        _mockCacheService.Setup(x => x.DetermineFileStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .Returns(BuildFileStatus.Added);

        _mockFileConversionService.Setup(x => x.ConvertFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, IProgress<double>, CancellationToken>((_, target, _, _, _) =>
            {
                var dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(target, "dummy converted content");
            })
            .ReturnsAsync(ConversionOperationResult.CreateSuccess());

        // Act
        var result = await _service.ExecuteBuildAsync(
            project,
            configuration,
            new List<string> { "GeneralsGamePatch2", "PatchINIOnly" },
            BuildStep.Build | BuildStep.Release);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        result.FilesProcessed.Should().BeGreaterOrEqualTo(4);
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithCreateManifest_CreatesLocalContentManifest()
    {
        // Arrange
        var fixture = CreateManifestFixture("ManifestProject", ["TestBundle"]);
        fixture.Project.Description = "Manifest test description";
        var bundlesDir = Path.Combine(fixture.BuildDir, ModBuilderConstants.BundlesSubdir);

        // Act
        var result = await _service.ExecuteBuildAsync(
            fixture.Project,
            fixture.Configuration,
            ["TestBundle"],
            BuildStep.CreateManifest);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        _mockLocalContentService.Verify(
            x => x.CreateLocalContentManifestAsync(
                It.IsAny<string>(),
                "ManifestProject",
                GenHub.Core.Models.Enums.ContentType.Mod,
                GameType.Generals,
                bundlesDir,
                It.IsAny<IProgress<ContentStorageProgress>?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                "1.0.0"),
            Times.Once);
        File.Exists(Path.Combine(fixture.BuildDir, ModBuilderConstants.ManifestFileName)).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithCustomContentType_PassesContentTypeToManifestCreation()
    {
        // Arrange
        var fixture = CreateManifestFixture(
            "PatchManifestProject",
            ["TestBundle"],
            "2.0.0",
            GenHub.Core.Models.Enums.ContentType.Patch);
        fixture.Project.Description = "Patch test description";
        var bundlesDir = Path.Combine(fixture.BuildDir, ModBuilderConstants.BundlesSubdir);

        // Act
        var result = await _service.ExecuteBuildAsync(
            fixture.Project,
            fixture.Configuration,
            ["TestBundle"],
            BuildStep.CreateManifest);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        _mockLocalContentService.Verify(
            x => x.CreateLocalContentManifestAsync(
                It.IsAny<string>(),
                "PatchManifestProject",
                GenHub.Core.Models.Enums.ContentType.Patch,
                GameType.Generals,
                bundlesDir,
                It.IsAny<IProgress<ContentStorageProgress>?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                "2.0.0"),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithMultiplePacksAndNoDefinitions_CombinesAllPacksIntoSingleManifest()
    {
        // Arrange
        var fixture = CreateManifestFixture("MultiPackProject", ["PackA", "PackB"]);

        // Act
        var result = await _service.ExecuteBuildAsync(
            fixture.Project,
            fixture.Configuration,
            ["PackA", "PackB"],
            BuildStep.CreateManifest);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        _mockLocalContentService.Verify(
            x => x.CreateLocalContentManifestAsync(
                It.IsAny<string>(),
                "MultiPackProject",
                GenHub.Core.Models.Enums.ContentType.Mod,
                GameType.Generals,
                It.IsAny<string?>(),
                It.IsAny<IProgress<ContentStorageProgress>?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()),
            Times.Once);
        File.Exists(Path.Combine(fixture.BuildDir, ModBuilderConstants.ManifestFileName)).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithPackSharingProjectName_DoesNotDuplicateManifestName()
    {
        // Arrange
        var fixture = CreateManifestFixture("GeneralsGamePatch2", ["GeneralsGamePatch2"]);

        // Act
        var result = await _service.ExecuteBuildAsync(
            fixture.Project,
            fixture.Configuration,
            ["GeneralsGamePatch2"],
            BuildStep.CreateManifest);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        _mockLocalContentService.Verify(
            x => x.CreateLocalContentManifestAsync(
                It.IsAny<string>(),
                "GeneralsGamePatch2",
                It.IsAny<GenHub.Core.Models.Enums.ContentType>(),
                It.IsAny<GameType>(),
                It.IsAny<string?>(),
                It.IsAny<IProgress<ContentStorageProgress>?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithManifestDefinition_CombinesLinkedPacksIntoSingleManifest()
    {
        // Arrange
        var fixture = CreateManifestFixture("CombinedProject", ["PackA", "PackB"]);
        fixture.Configuration.Manifests =
        [
            new()
            {
                Name = "Combined",
                Version = "2.5.0",
                Publisher = "acme",
                Description = "Groups both packs",
                PackNames = ["PackA", "PackB"],
            },
        ];

        // Act
        var result = await _service.ExecuteBuildAsync(
            fixture.Project,
            fixture.Configuration,
            ["PackA", "PackB"],
            BuildStep.CreateManifest);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        _mockLocalContentService.Verify(
            x => x.CreateLocalContentManifestAsync(
                It.IsAny<string>(),
                "Combined",
                GenHub.Core.Models.Enums.ContentType.Mod,
                GameType.Generals,
                It.IsAny<string?>(),
                It.IsAny<IProgress<ContentStorageProgress>?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                "acme",
                "2.5.0"),
            Times.Once);
        File.Exists(Path.Combine(fixture.BuildDir, ModBuilderConstants.ManifestFileName)).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithManifestDefinitionOverrides_UsesDefinitionContentTypeAndTargetGame()
    {
        // Arrange
        var fixture = CreateManifestFixture("OverrideProject", ["PackA", "PackB"]);
        fixture.Configuration.Manifests =
        [
            new()
            {
                Name = "RussianEdition",
                Version = "3.1.0",
                ContentType = GenHub.Core.Models.Enums.ContentType.LanguagePack,
                TargetGame = GameType.ZeroHour,
                PackNames = ["PackA"],
            },
        ];

        // Act
        var result = await _service.ExecuteBuildAsync(
            fixture.Project,
            fixture.Configuration,
            ["PackA"],
            BuildStep.CreateManifest);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        _mockLocalContentService.Verify(
            x => x.CreateLocalContentManifestAsync(
                It.IsAny<string>(),
                "RussianEdition",
                GenHub.Core.Models.Enums.ContentType.LanguagePack,
                GameType.ZeroHour,
                It.IsAny<string?>(),
                It.IsAny<IProgress<ContentStorageProgress>?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                "3.1.0"),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithManifestDefinitionReferencingUnknownPack_ReturnsFailure()
    {
        // Arrange
        var fixture = CreateManifestFixture("UnknownPackProject", ["PackA"]);
        fixture.Configuration.Manifests =
        [
            new()
            {
                Name = "Broken",
                PackNames = ["Nope"],
            },
        ];

        // Act
        var result = await _service.ExecuteBuildAsync(
            fixture.Project,
            fixture.Configuration,
            ["PackA"],
            BuildStep.CreateManifest);

        // Assert
        result.Success.Should().BeFalse();
        _mockLocalContentService.Verify(
            x => x.CreateLocalContentManifestAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<GenHub.Core.Models.Enums.ContentType>(),
                It.IsAny<GameType>(),
                It.IsAny<string?>(),
                It.IsAny<IProgress<ContentStorageProgress>?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithDuplicateManifestNames_ReturnsFailure()
    {
        // Arrange
        var fixture = CreateManifestFixture("DuplicateManifestProject", ["PackA"]);
        fixture.Configuration.Manifests =
        [
            new() { Name = "Same", PackNames = ["PackA"] },
            new() { Name = "Same", PackNames = ["PackA"] },
        ];

        // Act
        var result = await _service.ExecuteBuildAsync(
            fixture.Project,
            fixture.Configuration,
            ["PackA"],
            BuildStep.CreateManifest);

        // Assert
        result.Success.Should().BeFalse();
        _mockLocalContentService.Verify(
            x => x.CreateLocalContentManifestAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<GenHub.Core.Models.Enums.ContentType>(),
                It.IsAny<GameType>(),
                It.IsAny<string?>(),
                It.IsAny<IProgress<ContentStorageProgress>?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithBigBundlePack_CreatesBigArchiveInsteadOfZip()
    {
        // Arrange
        var patchProjectDir = Path.Combine(_tempDirectory, "GeneralsGamePatch2");
        var editedDir = Path.Combine(patchProjectDir, "GameFilesEdited");
        var buildDir = Path.Combine(patchProjectDir, ".Build");
        var releaseDir = Path.Combine(patchProjectDir, ".Release");

        Directory.CreateDirectory(Path.Combine(editedDir, "Data", "INI"));
        var iniFile = Path.Combine(editedDir, "Data", "INI", "GameData.ini");
        await File.WriteAllTextAsync(iniFile, "GameData content");

        var project = new ModBuilderProject
        {
            Name = "GeneralsGamePatch2",
            ProjectDir = patchProjectDir,
            Directories = new ProjectDirectories
            {
                GameFilesEdited = editedDir,
                Build = buildDir,
                Release = releaseDir,
            },
            BundleConfigs = new List<string>(),
        };

        var configuration = new BuildConfiguration
        {
            Folders = new FolderConfiguration
            {
                AbsBuildDir = buildDir,
                AbsReleaseDir = releaseDir,
            },
            Items = new List<BundleItem>
            {
                new()
                {
                    Name = "PatchINI",
                    IsBig = true,
                    Files = new List<BundleFile>
                    {
                        new() { AbsSourceParent = patchProjectDir, AbsSourceFile = iniFile, RelTargetFile = "Data/INI/GameData.ini" },
                    },
                },
            },
            Packs = new List<BundlePack>
            {
                new()
                {
                    Name = "CommunityPatch",
                    Big = true,
                    AllowBuild = true,
                    AllowInstall = true,
                    ItemNames = new List<string> { "PatchINI" },
                },
            },
        };

        _mockHashProvider.Setup(x => x.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("patchhash123");

        _mockCacheService.Setup(x => x.DetermineFileStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .Returns(BuildFileStatus.Added);

        _mockFileConversionService.Setup(x => x.ConvertFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConversionOperationResult.CreateSuccess());

        // Act
        var result = await _service.ExecuteBuildAsync(
            project,
            configuration,
            new List<string> { "CommunityPatch" },
            BuildStep.Build | BuildStep.Release);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        _mockArchiveService.Verify(
            x => x.CreateBigArchiveAsync(
                It.IsAny<string>(),
                It.Is<string>(p => p.EndsWith("CommunityPatch.big", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _mockArchiveService.Verify(
            x => x.CreateZipArchiveAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<System.IO.Compression.CompressionLevel>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithBigBundlePackCustomOutputFile_UsesCustomFileName()
    {
        // Arrange
        var patchProjectDir = Path.Combine(_tempDirectory, "GeneralsGamePatch2Custom");
        var editedDir = Path.Combine(patchProjectDir, "GameFilesEdited");
        var buildDir = Path.Combine(patchProjectDir, ".Build");
        var releaseDir = Path.Combine(patchProjectDir, ".Release");

        Directory.CreateDirectory(Path.Combine(editedDir, "Data", "INI"));
        var iniFile = Path.Combine(editedDir, "Data", "INI", "GameData.ini");
        await File.WriteAllTextAsync(iniFile, "GameData content");

        var project = new ModBuilderProject
        {
            Name = "GeneralsGamePatch2",
            ProjectDir = patchProjectDir,
            Directories = new ProjectDirectories
            {
                GameFilesEdited = editedDir,
                Build = buildDir,
                Release = releaseDir,
            },
            BundleConfigs = new List<string>(),
        };

        var configuration = new BuildConfiguration
        {
            Folders = new FolderConfiguration
            {
                AbsBuildDir = buildDir,
                AbsReleaseDir = releaseDir,
            },
            Items = new List<BundleItem>
            {
                new()
                {
                    Name = "PatchINI",
                    IsBig = true,
                    Files = new List<BundleFile>
                    {
                        new() { AbsSourceParent = patchProjectDir, AbsSourceFile = iniFile, RelTargetFile = "Data/INI/GameData.ini" },
                    },
                },
            },
            Packs = new List<BundlePack>
            {
                new()
                {
                    Name = "CommunityPatch",
                    OutputFile = "500_900_CommunityPatch_CoreINI.big",
                    AllowBuild = true,
                    AllowInstall = true,
                    ItemNames = new List<string> { "PatchINI" },
                },
            },
        };

        _mockHashProvider.Setup(x => x.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("patchhash123");

        _mockCacheService.Setup(x => x.DetermineFileStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .Returns(BuildFileStatus.Added);

        _mockFileConversionService.Setup(x => x.ConvertFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConversionOperationResult.CreateSuccess());

        // Act
        var result = await _service.ExecuteBuildAsync(
            project,
            configuration,
            new List<string> { "CommunityPatch" },
            BuildStep.Build | BuildStep.Release);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        _mockArchiveService.Verify(
            x => x.CreateBigArchiveAsync(
                It.IsAny<string>(),
                It.Is<string>(p => p.EndsWith("500_900_CommunityPatch_CoreINI.big", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteBuildAsync_WhenProjectIsInsideAppDirectory_ReturnsFailure()
    {
        // Arrange
        var appDirProject = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SampleProjects", "ModBuilder", "TestProject");
        var project = new ModBuilderProject
        {
            Name = "TestAppDirProject",
            ProjectDir = appDirProject,
            Directories = new ProjectDirectories
            {
                GameFilesEdited = Path.Combine(appDirProject, "GameFilesEdited"),
                Build = ".Build",
                Release = ".Release",
            },
        };

        var configuration = new BuildConfiguration();

        // Act
        var result = await _service.ExecuteBuildAsync(
            project,
            configuration,
            new List<string>(),
            BuildStep.Build);

        // Assert
        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("Cannot execute build within the application installation directory");
    }

    /// <summary>
    /// Tests that Clean step deletes the build directory when it is located inside the project directory.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task ExecuteBuildAsync_WithClean_WhenBuildDirIsInsideProject_DeletesBuildDirectory()
    {
        // Arrange
        var projectDir = Path.Combine(_tempDirectory, "CleanTestProject");
        var buildDir = Path.Combine(projectDir, ".Build");
        Directory.CreateDirectory(buildDir);
        var testFile = Path.Combine(buildDir, "test.txt");
        await File.WriteAllTextAsync(testFile, "data");

        var project = new ModBuilderProject
        {
            Name = "CleanTestProject",
            ProjectDir = projectDir,
            Directories = new ProjectDirectories
            {
                Build = ".Build",
                Release = ".Release",
                GameFilesEdited = "GameFilesEdited",
            },
        };

        var configuration = new BuildConfiguration
        {
            Folders = new FolderConfiguration
            {
                AbsBuildDir = buildDir,
            },
        };

        // Act
        var result = await _service.ExecuteBuildAsync(
            project,
            configuration,
            new List<string>(),
            BuildStep.Clean);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        Directory.Exists(buildDir).Should().BeFalse();
        _mockCacheService.Verify(c => c.Clear(), Times.Once);
    }

    /// <summary>
    /// Tests that Clean step skips deleting the build directory when it is located outside the project directory.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task ExecuteBuildAsync_WithClean_WhenBuildDirIsOutsideProject_SkipsDeletion()
    {
        // Arrange
        var projectDir = Path.Combine(_tempDirectory, "ProjectDir");
        var outsideDir = Path.Combine(_tempDirectory, "OutsideDir");
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(outsideDir);
        var testFile = Path.Combine(outsideDir, "important.txt");
        await File.WriteAllTextAsync(testFile, "do not delete");

        var project = new ModBuilderProject
        {
            Name = "OutsideTestProject",
            ProjectDir = projectDir,
            Directories = new ProjectDirectories
            {
                Build = outsideDir,
                Release = ".Release",
                GameFilesEdited = "GameFilesEdited",
            },
        };

        var configuration = new BuildConfiguration
        {
            Folders = new FolderConfiguration
            {
                AbsBuildDir = outsideDir,
            },
        };

        var reportedProgress = new List<BuildProgress>();
        var progressMock = new Mock<IProgress<BuildProgress>>();
        progressMock.Setup(p => p.Report(It.IsAny<BuildProgress>()))
            .Callback<BuildProgress>(reportedProgress.Add);

        // Act
        var result = await _service.ExecuteBuildAsync(
            project,
            configuration,
            new List<string>(),
            BuildStep.Clean,
            progressMock.Object);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        Directory.Exists(outsideDir).Should().BeTrue();
        File.Exists(testFile).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithBundleItems_ReportsFileTotalsAndPercent()
    {
        // Arrange
        var sourceFile = Path.Combine(_tempDirectory, "source.txt");
        await File.WriteAllTextAsync(sourceFile, "content");

        var project = new ModBuilderProject
        {
            Name = "TestProject",
            Directories = new ProjectDirectories
            {
                GameFilesEdited = _tempDirectory,
                Build = Path.Combine(_tempDirectory, "output")
            },
            BundleConfigs = new List<string>()
        };

        var configuration = new BuildConfiguration
        {
            Items = new List<BundleItem>
            {
                new()
                {
                    Name = "TestItem",
                    Files = new List<BundleFile>
                    {
                        new()
                        {
                            AbsSourceParent = _tempDirectory,
                            AbsSourceFile = sourceFile,
                            RelTargetFile = "output.txt"
                        }
                    }
                }
            },
            Packs = new List<BundlePack>
            {
                new() { Name = "TestPack", ItemNames = new List<string> { "TestItem" } }
            }
        };

        _mockCacheService.Setup(x => x.DetermineFileStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .Returns(BuildFileStatus.Added);

        var reportedProgress = new List<BuildProgress>();
        var progressLock = new object();
        var progressMock = new Mock<IProgress<BuildProgress>>();
        progressMock.Setup(p => p.Report(It.IsAny<BuildProgress>()))
            .Callback<BuildProgress>(p =>
            {
                lock (progressLock)
                {
                    reportedProgress.Add(p);
                }
            });

        // Act
        var result = await _service.ExecuteBuildAsync(project, configuration, new List<string> { "TestPack" }, BuildStep.Build, progressMock.Object);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        var fileReports = reportedProgress.Where(p => p.TotalFiles > 0).ToList();
        fileReports.Should().NotBeEmpty("per-file reports must carry totals");
        fileReports.Should().OnlyContain(p => p.PercentComplete >= 0 && p.PercentComplete <= 100);
        fileReports.Max(p => p.PercentComplete).Should().Be(100);
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithBigBundlePack_ReportsStagingAndPackingProgress()
    {
        // Arrange
        var patchProjectDir = Path.Combine(_tempDirectory, "StagingProgress");
        var editedDir = Path.Combine(patchProjectDir, "GameFilesEdited");
        var buildDir = Path.Combine(patchProjectDir, ".Build");
        var releaseDir = Path.Combine(patchProjectDir, ".Release");

        Directory.CreateDirectory(Path.Combine(editedDir, "Data"));
        var dataFile = Path.Combine(editedDir, "Data", "GameData.ini");
        await File.WriteAllTextAsync(dataFile, "GameData content");

        var project = new ModBuilderProject
        {
            Name = "StagingProgress",
            ProjectDir = patchProjectDir,
            Directories = new ProjectDirectories
            {
                GameFilesEdited = editedDir,
                Build = buildDir,
                Release = releaseDir,
            },
            BundleConfigs = new List<string>(),
        };

        var configuration = new BuildConfiguration
        {
            Folders = new FolderConfiguration
            {
                AbsBuildDir = buildDir,
                AbsReleaseDir = releaseDir,
            },
            Items = new List<BundleItem>
            {
                new()
                {
                    Name = "PatchINI",
                    IsBig = true,
                    Files = new List<BundleFile>
                    {
                        new() { AbsSourceParent = patchProjectDir, AbsSourceFile = dataFile, RelTargetFile = "Data/GameData.ini" },
                    },
                },
            },
            Packs = new List<BundlePack>
            {
                new()
                {
                    Name = "CommunityPatch",
                    Big = true,
                    AllowBuild = true,
                    AllowInstall = true,
                    ItemNames = new List<string> { "PatchINI" },
                },
            },
        };

        _mockCacheService.Setup(x => x.DetermineFileStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .Returns(BuildFileStatus.Added);

        _mockFileConversionService.Setup(x => x.ConvertFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConversionOperationResult.CreateSuccess());

        _mockArchiveService.Setup(x => x.CreateBigArchiveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, IProgress<double>?, CancellationToken>((_, target, progress, _) =>
            {
                progress?.Report(0.0);
                progress?.Report(0.5);
                progress?.Report(1.0);
                var dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                if (!File.Exists(target))
                {
                    File.WriteAllText(target, "dummy big content");
                }
            })
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateSuccess(true));

        var reportedProgress = new List<BuildProgress>();
        var progressLock = new object();
        var progressMock = new Mock<IProgress<BuildProgress>>();
        progressMock.Setup(p => p.Report(It.IsAny<BuildProgress>()))
            .Callback<BuildProgress>(p =>
            {
                lock (progressLock)
                {
                    reportedProgress.Add(p);
                }
            });

        // Act
        var result = await _service.ExecuteBuildAsync(
            project,
            configuration,
            new List<string> { "CommunityPatch" },
            BuildStep.Build | BuildStep.Release,
            progressMock.Object);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);

        // Archive progress is mapped through Progress<T>, which delivers asynchronously.
        var packingReports = new List<BuildProgress>();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            lock (progressLock)
            {
                packingReports = reportedProgress
                    .Where(p => p.CurrentStep.StartsWith("Packing CommunityPatch.big", StringComparison.Ordinal))
                    .ToList();
            }

            if (packingReports.Any(p => p.PercentComplete == 100))
            {
                break;
            }

            await Task.Delay(50);
        }

        List<string> reportedSteps;
        lock (progressLock)
        {
            reportedSteps = reportedProgress.Select(p => p.CurrentStep).ToList();
        }

        reportedSteps.Should().Contain(s => s.StartsWith("Staging release CommunityPatch", StringComparison.Ordinal));
        packingReports.Should().NotBeEmpty("release packing progress must be reported");
        packingReports.Should().Contain(p => p.PercentComplete == 100);
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithBigBundleItemManifest_PassesManifestToArchiveCreation()
    {
        // Arrange
        var patchProjectDir = Path.Combine(_tempDirectory, "ItemManifest");
        var editedDir = Path.Combine(patchProjectDir, "GameFilesEdited");
        var buildDir = Path.Combine(patchProjectDir, ".Build");
        var releaseDir = Path.Combine(patchProjectDir, ".Release");

        Directory.CreateDirectory(Path.Combine(editedDir, "Data", "INI"));
        var iniFile = Path.Combine(editedDir, "Data", "INI", "GameData.ini");
        await File.WriteAllTextAsync(iniFile, "GameData content");

        var project = new ModBuilderProject
        {
            Name = "ItemManifest",
            ProjectDir = patchProjectDir,
            Directories = new ProjectDirectories
            {
                GameFilesEdited = editedDir,
                Build = buildDir,
                Release = releaseDir,
            },
            BundleConfigs = new List<string>(),
        };

        var configuration = new BuildConfiguration
        {
            Folders = new FolderConfiguration
            {
                AbsBuildDir = buildDir,
                AbsReleaseDir = releaseDir,
            },
            Items = new List<BundleItem>
            {
                new()
                {
                    Name = "PatchINI",
                    IsBig = true,
                    ManifestFile = "config/500_900_CommunityPatch_CoreINI.big.manifest.json",
                    Files = new List<BundleFile>
                    {
                        new() { AbsSourceParent = patchProjectDir, AbsSourceFile = iniFile, RelTargetFile = "Data/INI/GameData.ini" },
                    },
                },
            },
            Packs = new List<BundlePack>(),
        };

        _mockCacheService.Setup(x => x.DetermineFileStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .Returns(BuildFileStatus.Added);

        // Act
        var result = await _service.ExecuteBuildAsync(
            project,
            configuration,
            new List<string>(),
            BuildStep.Build);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        var expectedManifest = Path.Combine(patchProjectDir, "config", "500_900_CommunityPatch_CoreINI.big.manifest.json");
        _mockArchiveService.Verify(
            x => x.CreateBigArchiveAsync(
                It.IsAny<string>(),
                It.Is<string>(p => p.EndsWith("PatchINI.big", StringComparison.OrdinalIgnoreCase)),
                It.Is<string>(p => p != null && string.Equals(Path.GetFullPath(p), Path.GetFullPath(expectedManifest), StringComparison.OrdinalIgnoreCase)),
                It.IsAny<IProgress<double>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithTgaMatchingOutputFormat_CopiesVerbatimWithoutConversion()
    {
        // Arrange
        var sourceFile = Path.Combine(_tempDirectory, "texture.tga");
        var sourceBytes = new byte[] { 0x54, 0x47, 0x41, 0x00, 0x01, 0x02 };
        await File.WriteAllBytesAsync(sourceFile, sourceBytes);

        var project = new ModBuilderProject
        {
            Name = "TgaPassthrough",
            ProjectDir = _tempDirectory,
            Directories = new ProjectDirectories
            {
                GameFilesEdited = _tempDirectory,
                Build = Path.Combine(_tempDirectory, "output")
            },
            BundleConfigs = new List<string>()
        };

        var configuration = new BuildConfiguration
        {
            Items = new List<BundleItem>
            {
                new()
                {
                    Name = "Indicators",
                    IsBig = false,
                    Files = new List<BundleFile>
                    {
                        new()
                        {
                            AbsSourceParent = _tempDirectory,
                            AbsSourceFile = sourceFile,
                            RelTargetFile = "Art/Textures/texture.tga",
                            Params = new Dictionary<string, object> { ["outputformat"] = "TGA" },
                        }
                    }
                }
            },
            Packs = new List<BundlePack>(),
        };

        _mockCacheService.Setup(x => x.DetermineFileStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .Returns(BuildFileStatus.Added);

        // Act
        var result = await _service.ExecuteBuildAsync(project, configuration, new List<string>(), BuildStep.Build);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        _mockFileConversionService.Verify(
            x => x.ConvertFileAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        var rawCopies = Directory.GetFiles(Path.Combine(_tempDirectory, "output"), "*.tga", SearchOption.AllDirectories);
        rawCopies.Should().HaveCount(1);
        (await File.ReadAllBytesAsync(rawCopies[0])).Should().Equal(sourceBytes);
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithTgaAndNoOutputFormat_ConvertsToDds()
    {
        // Arrange: legacy default converts images to DDS when no output format is declared.
        var sourceFile = Path.Combine(_tempDirectory, "legacy.tga");
        await File.WriteAllBytesAsync(sourceFile, new byte[] { 0x54, 0x47, 0x41 });

        var project = new ModBuilderProject
        {
            Name = "TgaLegacy",
            ProjectDir = _tempDirectory,
            Directories = new ProjectDirectories
            {
                GameFilesEdited = _tempDirectory,
                Build = Path.Combine(_tempDirectory, "output")
            },
            BundleConfigs = new List<string>()
        };

        var configuration = new BuildConfiguration
        {
            Items = new List<BundleItem>
            {
                new()
                {
                    Name = "Indicators",
                    IsBig = false,
                    Files = new List<BundleFile>
                    {
                        new()
                        {
                            AbsSourceParent = _tempDirectory,
                            AbsSourceFile = sourceFile,
                            RelTargetFile = "Art/Textures/legacy.tga",
                        }
                    }
                }
            },
            Packs = new List<BundlePack>(),
        };

        _mockCacheService.Setup(x => x.DetermineFileStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .Returns(BuildFileStatus.Added);

        _mockFileConversionService.Setup(x => x.ConvertFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConversionOperationResult.CreateSuccess());

        // Act
        var result = await _service.ExecuteBuildAsync(project, configuration, new List<string>(), BuildStep.Build);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        _mockFileConversionService.Verify(
            x => x.ConvertFileAsync(
                sourceFile,
                It.Is<string>(p => p.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<string>(),
                It.IsAny<IProgress<double>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void ToRelativeTargetPath_WithRelativePath_ReturnsUnchanged()
    {
        BuildEngineService.ToRelativeTargetPath("Data/INI/GameData.ini").Should().Be("Data/INI/GameData.ini");
        BuildEngineService.ToRelativeTargetPath(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void ToRelativeTargetPath_WithAbsolutePath_StripsRoot()
    {
        // Arrange
        var absolute = Path.Combine(_tempDirectory, "GameFilesEdited", "Data", "a.ini");

        // Act
        var result = BuildEngineService.ToRelativeTargetPath(absolute);

        // Assert
        Path.IsPathRooted(result).Should().BeFalse();
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithCollidingTargets_ProducesSingleDeterministicCopyAsync()
    {
        // Arrange: two sources mapping to one target (legacy flattened configs).
        var winnerSource = Path.Combine(_tempDirectory, "a-first.txt");
        var loserSource = Path.Combine(_tempDirectory, "b-second.txt");
        await File.WriteAllTextAsync(winnerSource, "winner");
        await File.WriteAllTextAsync(loserSource, "loser");

        var project = new ModBuilderProject
        {
            Name = "TestProject",
            Directories = new ProjectDirectories
            {
                GameFilesEdited = _tempDirectory,
                Build = Path.Combine(_tempDirectory, "output"),
            },
            BundleConfigs = new List<string>(),
        };

        var configuration = new BuildConfiguration
        {
            Items = new List<BundleItem>
            {
                new()
                {
                    Name = "TestItem",
                    IsBig = false,
                    Files = new List<BundleFile>
                    {
                        new() { AbsSourceParent = _tempDirectory, AbsSourceFile = winnerSource, RelTargetFile = "shared.txt" },
                        new() { AbsSourceParent = _tempDirectory, AbsSourceFile = loserSource, RelTargetFile = "shared.txt" },
                    },
                },
            },
            Packs = new List<BundlePack>(),
        };

        _mockCacheService.Setup(x => x.DetermineFileStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .Returns(BuildFileStatus.Added);

        // Act
        var result = await _service.ExecuteBuildAsync(project, configuration, new List<string>(), BuildStep.Build);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        result.FilesFailed.Should().Be(0);
        var target = Path.Combine(_tempDirectory, "output", ModBuilderConstants.RawBundleItemsSubdir, "shared.txt");
        (await File.ReadAllTextAsync(target)).Should().Be("winner");
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithDuplicateItemNames_FailsWithActionableErrorAsync()
    {
        // Arrange: duplicates that differ only by case are ambiguous too,
        // because pack references resolve case-insensitively.
        var project = new ModBuilderProject
        {
            Name = "TestProject",
            Directories = new ProjectDirectories
            {
                GameFilesEdited = _tempDirectory,
                Build = Path.Combine(_tempDirectory, "output"),
            },
            BundleConfigs = new List<string>(),
        };

        var configuration = new BuildConfiguration
        {
            Items = new List<BundleItem>
            {
                new() { Name = "CoreData", IsBig = false },
                new() { Name = "coredata", IsBig = false },
            },
            Packs = new List<BundlePack>(),
        };

        // Act
        var result = await _service.ExecuteBuildAsync(project, configuration, new List<string>(), BuildStep.Build);

        // Assert
        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("CoreData");
        result.FirstError.Should().Contain("ModBundleItems.json");
        result.FirstError.Should().StartWith("Invalid bundle configuration");
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithDuplicatePackNames_FailsWithActionableErrorAsync()
    {
        // Arrange
        var project = new ModBuilderProject
        {
            Name = "TestProject",
            Directories = new ProjectDirectories
            {
                GameFilesEdited = _tempDirectory,
                Build = Path.Combine(_tempDirectory, "output"),
            },
            BundleConfigs = new List<string>(),
        };

        var configuration = new BuildConfiguration
        {
            Items = new List<BundleItem>
            {
                new() { Name = "CoreData", IsBig = false },
            },
            Packs = new List<BundlePack>
            {
                new() { Name = "Release", ItemNames = new List<string> { "CoreData" } },
                new() { Name = "Release", ItemNames = new List<string> { "CoreData" } },
            },
        };

        // Act
        var result = await _service.ExecuteBuildAsync(project, configuration, new List<string>(), BuildStep.Build);

        // Assert
        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("Release");
        result.FirstError.Should().Contain("ModBundlePacks.json");
        result.FirstError.Should().StartWith("Invalid bundle configuration");
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithAbsoluteSelfTarget_SucceedsWithoutFailureAsync()
    {
        // Arrange: corrupted configs resolve targets onto their own source path.
        var sourceFile = Path.Combine(_tempDirectory, "self.txt");
        await File.WriteAllTextAsync(sourceFile, "content");

        var project = new ModBuilderProject
        {
            Name = "TestProject",
            Directories = new ProjectDirectories
            {
                GameFilesEdited = _tempDirectory,
                Build = Path.Combine(_tempDirectory, "output"),
            },
            BundleConfigs = new List<string>(),
        };

        var configuration = new BuildConfiguration
        {
            Items = new List<BundleItem>
            {
                new()
                {
                    Name = "TestItem",
                    IsBig = false,
                    Files = new List<BundleFile>
                    {
                        new() { AbsSourceParent = _tempDirectory, AbsSourceFile = sourceFile, RelTargetFile = sourceFile },
                    },
                },
            },
            Packs = new List<BundlePack>(),
        };

        _mockCacheService.Setup(x => x.DetermineFileStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .Returns(BuildFileStatus.Added);

        // Act
        var result = await _service.ExecuteBuildAsync(project, configuration, new List<string>(), BuildStep.Build);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        result.FilesFailed.Should().Be(0);
    }

    private (ModBuilderProject Project, BuildConfiguration Configuration, string BuildDir) CreateManifestFixture(
        string projectName,
        string[] packNames,
        string projectVersion = "1.0.0",
        GenHub.Core.Models.Enums.ContentType contentType = GenHub.Core.Models.Enums.ContentType.Mod)
    {
        var projectDir = Path.Combine(_tempDirectory, projectName);
        Directory.CreateDirectory(projectDir);
        var buildDir = Path.Combine(projectDir, ".Build");
        var bundlesDir = Path.Combine(buildDir, ModBuilderConstants.BundlesSubdir);
        Directory.CreateDirectory(bundlesDir);

        var items = new List<BundleItem>();
        var packs = new List<BundlePack>();
        foreach (var packName in packNames)
        {
            var itemName = $"{packName}Item";
            var sourceFile = Path.Combine(projectDir, $"{itemName}.txt");
            File.WriteAllText(sourceFile, "content");
            items.Add(new BundleItem
            {
                Name = itemName,
                Files = new List<BundleFile>
                {
                    new()
                    {
                        AbsSourceParent = projectDir,
                        AbsSourceFile = sourceFile,
                        RelTargetFile = $"{itemName}.txt",
                    },
                },
            });
            packs.Add(new BundlePack { Name = packName, ItemNames = new List<string> { itemName } });
        }

        var project = new ModBuilderProject
        {
            Name = projectName,
            Version = projectVersion,
            ProjectDir = projectDir,
            TargetGame = GameType.Generals,
            ContentType = contentType,
            Directories = new ProjectDirectories
            {
                Build = ".Build",
                Release = ".Release",
                GameFilesEdited = "GameFilesEdited",
            },
            BundleConfigs = new List<string>(),
        };

        var configuration = new BuildConfiguration
        {
            Items = items,
            Packs = packs,
            Folders = new FolderConfiguration
            {
                AbsBuildDir = buildDir,
            },
        };

        return (project, configuration, buildDir);
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithTraversalRelTarget_FailsWithoutWritingOutsideStaging()
    {
        // Arrange
        var patchProjectDir = Path.Combine(_tempDirectory, "TraversalRelTarget");
        var editedDir = Path.Combine(patchProjectDir, "GameFilesEdited");
        var buildDir = Path.Combine(patchProjectDir, ".Build");
        var releaseDir = Path.Combine(patchProjectDir, ".Release");

        Directory.CreateDirectory(Path.Combine(editedDir, "Data"));
        var iniFile = Path.Combine(editedDir, "Data", "GameData.ini");
        await File.WriteAllTextAsync(iniFile, "GameData content");

        var project = new ModBuilderProject
        {
            Name = "TraversalRelTarget",
            ProjectDir = patchProjectDir,
            Directories = new ProjectDirectories
            {
                GameFilesEdited = editedDir,
                Build = buildDir,
                Release = releaseDir,
            },
            BundleConfigs = new List<string>(),
        };

        var configuration = new BuildConfiguration
        {
            Folders = new FolderConfiguration
            {
                AbsBuildDir = buildDir,
                AbsReleaseDir = releaseDir,
            },
            Items = new List<BundleItem>
            {
                new()
                {
                    Name = "EvilItem",
                    IsBig = true,
                    Files = new List<BundleFile>
                    {
                        new() { AbsSourceParent = patchProjectDir, AbsSourceFile = iniFile, RelTargetFile = "../../evil.txt" },
                    },
                },
            },
            Packs = new List<BundlePack>(),
        };

        _mockCacheService.Setup(x => x.DetermineFileStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .Returns(BuildFileStatus.Added);

        // Act
        var result = await _service.ExecuteBuildAsync(
            project,
            configuration,
            new List<string>(),
            BuildStep.Build);

        // Assert
        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("escapes");
        Directory.GetFiles(patchProjectDir, "evil.txt", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithTraversalBigSuffix_FailsWithoutWritingOutsideBundles()
    {
        // Arrange
        var patchProjectDir = Path.Combine(_tempDirectory, "TraversalBigSuffix");
        var editedDir = Path.Combine(patchProjectDir, "GameFilesEdited");
        var buildDir = Path.Combine(patchProjectDir, ".Build");
        var releaseDir = Path.Combine(patchProjectDir, ".Release");

        Directory.CreateDirectory(Path.Combine(editedDir, "Data"));
        var iniFile = Path.Combine(editedDir, "Data", "GameData.ini");
        await File.WriteAllTextAsync(iniFile, "GameData content");

        var project = new ModBuilderProject
        {
            Name = "TraversalBigSuffix",
            ProjectDir = patchProjectDir,
            Directories = new ProjectDirectories
            {
                GameFilesEdited = editedDir,
                Build = buildDir,
                Release = releaseDir,
            },
            BundleConfigs = new List<string>(),
        };

        var configuration = new BuildConfiguration
        {
            Folders = new FolderConfiguration
            {
                AbsBuildDir = buildDir,
                AbsReleaseDir = releaseDir,
            },
            Items = new List<BundleItem>
            {
                new()
                {
                    Name = "EvilSuffix",
                    IsBig = true,
                    BigSuffix = "/../../evil",
                    Files = new List<BundleFile>
                    {
                        new() { AbsSourceParent = patchProjectDir, AbsSourceFile = iniFile, RelTargetFile = "Data/GameData.ini" },
                    },
                },
            },
            Packs = new List<BundlePack>(),
        };

        _mockCacheService.Setup(x => x.DetermineFileStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .Returns(BuildFileStatus.Added);

        // Act
        var result = await _service.ExecuteBuildAsync(
            project,
            configuration,
            new List<string>(),
            BuildStep.Build);

        // Assert
        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("outside the bundles directory");
        Directory.GetFiles(patchProjectDir, "evil.big", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithTraversalPackName_FailsWithoutCreatingOutsideDirs()
    {
        // Arrange
        var patchProjectDir = Path.Combine(_tempDirectory, "TraversalPack");
        var editedDir = Path.Combine(patchProjectDir, "GameFilesEdited");
        var buildDir = Path.Combine(patchProjectDir, ".Build");
        var releaseDir = Path.Combine(patchProjectDir, ".Release");

        Directory.CreateDirectory(Path.Combine(editedDir, "Data"));
        var iniFile = Path.Combine(editedDir, "Data", "GameData.ini");
        await File.WriteAllTextAsync(iniFile, "GameData content");

        var project = new ModBuilderProject
        {
            Name = "TraversalPack",
            ProjectDir = patchProjectDir,
            Directories = new ProjectDirectories
            {
                GameFilesEdited = editedDir,
                Build = buildDir,
                Release = releaseDir,
            },
            BundleConfigs = new List<string>(),
        };

        var configuration = new BuildConfiguration
        {
            Folders = new FolderConfiguration
            {
                AbsBuildDir = buildDir,
                AbsReleaseDir = releaseDir,
            },
            Items = new List<BundleItem>
            {
                new()
                {
                    Name = "GoodItem",
                    IsBig = false,
                    Files = new List<BundleFile>
                    {
                        new() { AbsSourceParent = patchProjectDir, AbsSourceFile = iniFile, RelTargetFile = "Data/GameData.ini" },
                    },
                },
            },
            Packs = new List<BundlePack>
            {
                new() { Name = "a/../../evilpack", AllowBuild = true, ItemNames = new List<string> { "GoodItem" } },
            },
        };

        _mockCacheService.Setup(x => x.DetermineFileStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .Returns(BuildFileStatus.Added);

        // Act
        var result = await _service.ExecuteBuildAsync(
            project,
            configuration,
            new List<string> { "a/../../evilpack" },
            BuildStep.Build | BuildStep.Release);

        // Assert
        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("outside the build directories");
        Directory.Exists(Path.Combine(patchProjectDir, "evilpack")).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithTgaBigBundleItem_StagesDdsInsteadOfTga()
    {
        // Arrange
        var patchProjectDir = Path.Combine(_tempDirectory, "TgaBigItem");
        var editedDir = Path.Combine(patchProjectDir, "GameFilesEdited");
        var buildDir = Path.Combine(patchProjectDir, ".Build");
        var releaseDir = Path.Combine(patchProjectDir, ".Release");

        Directory.CreateDirectory(Path.Combine(editedDir, "Art", "Textures"));
        var tgaFile = Path.Combine(editedDir, "Art", "Textures", "Unit.tga");
        await File.WriteAllBytesAsync(tgaFile, new byte[] { 0x54, 0x47, 0x41 });

        var project = new ModBuilderProject
        {
            Name = "TgaBigItem",
            ProjectDir = patchProjectDir,
            Directories = new ProjectDirectories
            {
                GameFilesEdited = editedDir,
                Build = buildDir,
                Release = releaseDir,
            },
            BundleConfigs = new List<string>(),
        };

        var configuration = new BuildConfiguration
        {
            Folders = new FolderConfiguration
            {
                AbsBuildDir = buildDir,
                AbsReleaseDir = releaseDir,
            },
            Items = new List<BundleItem>
            {
                new()
                {
                    Name = "UnitTextures",
                    IsBig = true,
                    Files = new List<BundleFile>
                    {
                        new()
                        {
                            AbsSourceParent = patchProjectDir,
                            AbsSourceFile = tgaFile,
                            RelTargetFile = "Art/Textures/Unit.tga",
                        },
                    },
                },
            },
            Packs = new List<BundlePack>(),
        };

        _mockCacheService.Setup(x => x.DetermineFileStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .Returns(BuildFileStatus.Added);

        _mockFileConversionService.Setup(x => x.ConvertFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, IProgress<double>, CancellationToken>((_, target, _, _, _) =>
            {
                var dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllBytes(target, new byte[] { 0x44, 0x44, 0x53 });
            })
            .ReturnsAsync(ConversionOperationResult.CreateSuccess());

        bool? stagedDdsExists = null;
        bool? stagedTgaExists = null;
        _mockArchiveService.Setup(x => x.CreateBigArchiveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, IProgress<double>?, CancellationToken>((sourceDir, target, _, _) =>
            {
                var dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                if (!File.Exists(target))
                {
                    File.WriteAllText(target, "dummy big content");
                }
                stagedDdsExists = File.Exists(Path.Combine(sourceDir, "Art", "Textures", "Unit.dds"));
                stagedTgaExists = File.Exists(Path.Combine(sourceDir, "Art", "Textures", "Unit.tga"));
            })
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await _service.ExecuteBuildAsync(project, configuration, new List<string>(), BuildStep.Build);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        stagedDdsExists.Should().BeTrue("converted DDS asset must be staged into the Big archive");
        stagedTgaExists.Should().BeFalse("unconverted TGA asset must not be staged into the Big archive");
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithMissingConvertedDds_FailsBuildAndRefusesToPackSource()
    {
        // Arrange
        var patchProjectDir = Path.Combine(_tempDirectory, "MissingDdsBigItem");
        var editedDir = Path.Combine(patchProjectDir, "GameFilesEdited");
        var buildDir = Path.Combine(patchProjectDir, ".Build");
        var releaseDir = Path.Combine(patchProjectDir, ".Release");

        Directory.CreateDirectory(Path.Combine(editedDir, "Art", "Textures"));
        var tgaFile = Path.Combine(editedDir, "Art", "Textures", "Unit.tga");
        await File.WriteAllBytesAsync(tgaFile, new byte[] { 0x54, 0x47, 0x41 });

        var project = new ModBuilderProject
        {
            Name = "MissingDdsBigItem",
            ProjectDir = patchProjectDir,
            Directories = new ProjectDirectories
            {
                GameFilesEdited = editedDir,
                Build = buildDir,
                Release = releaseDir,
            },
            BundleConfigs = new List<string>(),
        };

        var configuration = new BuildConfiguration
        {
            Folders = new FolderConfiguration
            {
                AbsBuildDir = buildDir,
                AbsReleaseDir = releaseDir,
            },
            Items = new List<BundleItem>
            {
                new()
                {
                    Name = "UnitTextures",
                    IsBig = true,
                    Files = new List<BundleFile>
                    {
                        new()
                        {
                            AbsSourceParent = patchProjectDir,
                            AbsSourceFile = tgaFile,
                            RelTargetFile = "Art/Textures/Unit.tga",
                        },
                    },
                },
            },
            Packs = new List<BundlePack>(),
        };

        _mockCacheService.Setup(x => x.DetermineFileStatus(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .Returns(BuildFileStatus.Added);

        // Simulate conversion claiming success but not actually writing the DDS file to disk
        _mockFileConversionService.Setup(x => x.ConvertFileAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConversionOperationResult.CreateSuccess());

        // Act
        var result = await _service.ExecuteBuildAsync(project, configuration, new List<string>(), BuildStep.Build);

        // Assert
        result.Success.Should().BeFalse("build must fail when expected converted asset is missing");
        result.FirstError.Should().Contain("Unit.tga");
        _mockArchiveService.Verify(
            x => x.CreateBigArchiveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "archive packing must not be called when converted asset is missing");
    }
}
