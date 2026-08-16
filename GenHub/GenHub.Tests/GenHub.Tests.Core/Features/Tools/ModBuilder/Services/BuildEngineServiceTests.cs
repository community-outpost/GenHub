using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Tools.ModBuilder;
using GenHub.Features.Tools.ModBuilder.Services;
using Microsoft.Extensions.Logging;
using Moq;

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
        _mockLogger = new Mock<ILogger<BuildEngineService>>();
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);

        _mockConfigurationLoaderService.Setup(x => x.ResolveWildcardsAsync(It.IsAny<BuildConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BuildConfiguration config, CancellationToken ct) => config);

        _mockArchiveService.Setup(x => x.CreateBigArchiveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateSuccess(true));

        _mockArchiveService.Setup(x => x.CreateZipArchiveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<System.IO.Compression.CompressionLevel>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateSuccess(true));

        _service = new BuildEngineService(
            _mockCacheService.Object,
            _mockFileConversionService.Object,
            _mockHashProvider.Object,
            _mockConfigurationLoaderService.Object,
            _mockArchiveService.Object,
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
        result.Success.Should().BeTrue();
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
        var progressReported = false;
        var progress = new Progress<string>(p => progressReported = true);

        // Act
        var result = await _service.ExecuteBuildAsync(project, configuration, selectedPacks, BuildStep.Build, progress);

        // Assert
        result.Success.Should().BeTrue();
        progressReported.Should().BeTrue();
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
        await _service.AbortAsync();

        // Assert - No exception should be thrown
    }

    [Fact]
    public void InvalidateBuildStructureCache_ClearsCache()
    {
        // Act
        _service.InvalidateBuildStructureCache();

        // Assert - No exception should be thrown
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
            .ReturnsAsync(new ConversionOperationResult { Success = true });

        // Act
        var result = await _service.ExecuteBuildAsync(project, configuration, selectedPacks, BuildStep.Build);

        // Assert
        result.Success.Should().BeTrue();
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

        // Act
        var result = await _service.ExecuteBuildAsync(project, configuration, selectedPacks, BuildStep.Build);

        // Assert
        result.Success.Should().BeTrue();
        result.FilesSkipped.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithFailedConversion_IncrementsFailedCount()
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
            .ReturnsAsync(new ConversionOperationResult { Success = false, Errors = new List<string> { "Conversion failed" } });

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
        result.Success.Should().BeTrue();
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
            .ReturnsAsync(new ConversionOperationResult { Success = true });

        // Act
        var result = await _service.ExecuteBuildAsync(project, configuration, selectedPacks, BuildStep.Build);

        // Assert
        result.Success.Should().BeTrue();
        result.FilesProcessed.Should().BeGreaterOrEqualTo(2);
    }
}
