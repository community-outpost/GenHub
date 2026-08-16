using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Models.Tools.ModBuilder;
using GenHub.Features.Tools.ModBuilder.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.Services;

/// <summary>
/// Unit tests for <see cref="ConfigurationLoaderService"/>.
/// </summary>
public sealed class ConfigurationLoaderServiceTests : IDisposable
{
    private readonly Mock<ILogger<ConfigurationLoaderService>> _mockLogger;
    private readonly ConfigurationLoaderService _service;
    private readonly string _tempDirectory;

    public ConfigurationLoaderServiceTests()
    {
        _mockLogger = new Mock<ILogger<ConfigurationLoaderService>>();
        _service = new ConfigurationLoaderService(_mockLogger.Object);
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
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
        var service = new ConfigurationLoaderService(_mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public async Task LoadConfigurationAsync_WithValidConfig_ReturnsConfiguration()
    {
        // Arrange
        var configPath = Path.Combine(_tempDirectory, "config.json");
        var config = new BuildConfiguration
        {
            Items = new List<BundleItem>
            {
                new() { Name = "TestItem", Files = new List<BundleFile>() }
            },
            Packs = new List<BundlePack>
            {
                new() { Name = "TestPack", ItemNames = new List<string> { "TestItem" } }
            }
        };
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(configPath, json);

        // Act
        var result = await _service.LoadConfigurationAsync(configPath);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);
        result.Items[0].Name.Should().Be("TestItem");
        result.Packs.Should().Contain(pack => pack.Name == "TestPack");
        result.LoadedConfigFiles.Should().Contain(configPath);
    }

    [Fact]
    public async Task LoadConfigurationAsync_WithNonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var configPath = Path.Combine(_tempDirectory, "nonexistent.json");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            async () => await _service.LoadConfigurationAsync(configPath));
    }

    [Fact]
    public async Task LoadConfigurationAsync_WithInvalidJson_ThrowsInvalidOperationException()
    {
        // Arrange
        var configPath = Path.Combine(_tempDirectory, "invalid.json");
        await File.WriteAllTextAsync(configPath, "{ invalid json }");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _service.LoadConfigurationAsync(configPath));
    }

    [Fact]
    public async Task LoadConfigurationAsync_WithEmptyFile_ThrowsInvalidOperationException()
    {
        // Arrange
        var configPath = Path.Combine(_tempDirectory, "empty.json");
        await File.WriteAllTextAsync(configPath, string.Empty);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _service.LoadConfigurationAsync(configPath));
    }

    [Fact]
    public async Task LoadConfigurationAsync_WithComments_IgnoresComments()
    {
        // Arrange
        var configPath = Path.Combine(_tempDirectory, "config.json");
        var json = @"{
            // This is a comment
            ""items"": [],
            ""packs"": {}
        }";
        await File.WriteAllTextAsync(configPath, json);

        // Act
        var result = await _service.LoadConfigurationAsync(configPath);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().BeEmpty();
        result.Packs.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadConfigurationAsync_WithTrailingCommas_HandlesCorrectly()
    {
        // Arrange
        var configPath = Path.Combine(_tempDirectory, "config.json");
        var json = @"{
            ""items"": [
                { ""name"": ""Item1"", ""files"": [] },
            ],
            ""packs"": {},
        }";
        await File.WriteAllTextAsync(configPath, json);

        // Act
        var result = await _service.LoadConfigurationAsync(configPath);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task LoadAndMergeConfigurationsAsync_WithEmptyList_ReturnsEmptyConfiguration()
    {
        // Arrange
        var configPaths = new List<string>();

        // Act
        var result = await _service.LoadAndMergeConfigurationsAsync(configPaths);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().BeEmpty();
        result.Packs.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAndMergeConfigurationsAsync_WithSingleConfig_ReturnsSameConfig()
    {
        // Arrange
        var configPath = Path.Combine(_tempDirectory, "config.json");
        var config = new BuildConfiguration
        {
            Items = new List<BundleItem>
            {
                new() { Name = "TestItem", Files = new List<BundleFile>() }
            }
        };
        var json = JsonSerializer.Serialize(config);
        await File.WriteAllTextAsync(configPath, json);

        // Act
        var result = await _service.LoadAndMergeConfigurationsAsync(new[] { configPath });

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);
        result.Items[0].Name.Should().Be("TestItem");
    }

    [Fact]
    public async Task LoadAndMergeConfigurationsAsync_WithMultipleConfigs_MergesCorrectly()
    {
        // Arrange
        var config1Path = Path.Combine(_tempDirectory, "config1.json");
        var config1 = new BuildConfiguration
        {
            Items = new List<BundleItem>
            {
                new() { Name = "Item1", Files = new List<BundleFile>() }
            },
            Packs = new List<BundlePack>
            {
                new() { Name = "Pack1", ItemNames = new List<string> { "Item1" } }
            }
        };
        await File.WriteAllTextAsync(config1Path, JsonSerializer.Serialize(config1));

        var config2Path = Path.Combine(_tempDirectory, "config2.json");
        var config2 = new BuildConfiguration
        {
            Items = new List<BundleItem>
            {
                new() { Name = "Item2", Files = new List<BundleFile>() }
            },
            Packs = new List<BundlePack>
            {
                new() { Name = "Pack2", ItemNames = new List<string> { "Item2" } }
            }
        };
        await File.WriteAllTextAsync(config2Path, JsonSerializer.Serialize(config2));

        // Act
        var result = await _service.LoadAndMergeConfigurationsAsync(new[] { config1Path, config2Path });

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(2);
        result.Items.Select(i => i.Name).Should().Contain(new[] { "Item1", "Item2" });
        result.Packs.Should().Contain(pack => pack.Name == "Pack1" || pack.Name == "Pack2");
        result.LoadedConfigFiles.Should().Contain(config1Path);
        result.LoadedConfigFiles.Should().Contain(config2Path);
    }

    [Fact]
    public async Task ResolveWildcardsAsync_WithNoWildcards_ReturnsUnchanged()
    {
        // Arrange
        var testFile = Path.Combine(_tempDirectory, "test.txt");
        await File.WriteAllTextAsync(testFile, "content");

        var config = new BuildConfiguration
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
                            AbsSourceFile = testFile,
                            RelTargetFile = "test.txt"
                        }
                    }
                }
            }
        };

        // Act
        var result = await _service.ResolveWildcardsAsync(config);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);
        result.Items[0].Files.Should().HaveCount(1);
        result.Items[0].Files[0].AbsSourceFile.Should().Be(testFile);
    }

    [Fact]
    public async Task ResolveWildcardsAsync_WithWildcardPattern_ResolvesMultipleFiles()
    {
        // Arrange
        var file1 = Path.Combine(_tempDirectory, "test1.txt");
        var file2 = Path.Combine(_tempDirectory, "test2.txt");
        await File.WriteAllTextAsync(file1, "content1");
        await File.WriteAllTextAsync(file2, "content2");

        var wildcardPattern = Path.Combine(_tempDirectory, "*.txt");
        var config = new BuildConfiguration
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
                            AbsSourceFile = wildcardPattern,
                            RelTargetFile = "output"
                        }
                    }
                }
            }
        };

        // Act
        var result = await _service.ResolveWildcardsAsync(config);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);
        result.Items[0].Files.Should().HaveCountGreaterOrEqualTo(2);
        result.Items[0].Files.Select(f => f.AbsSourceFile).Should().Contain(file1);
        result.Items[0].Files.Select(f => f.AbsSourceFile).Should().Contain(file2);
    }

    [Fact]
    public async Task ResolveWildcardsAsync_WithNestedWildcards_ResolvesRecursively()
    {
        // Arrange
        var subDir = Path.Combine(_tempDirectory, "subdir");
        Directory.CreateDirectory(subDir);
        var file1 = Path.Combine(_tempDirectory, "test.txt");
        var file2 = Path.Combine(subDir, "test.txt");
        await File.WriteAllTextAsync(file1, "content1");
        await File.WriteAllTextAsync(file2, "content2");

        var wildcardPattern = Path.Combine(_tempDirectory, "**", "*.txt");
        var config = new BuildConfiguration
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
                            AbsSourceFile = wildcardPattern,
                            RelTargetFile = "output"
                        }
                    }
                }
            }
        };

        // Act
        var result = await _service.ResolveWildcardsAsync(config);

        // Assert
        result.Should().NotBeNull();
        result.Items[0].Files.Should().HaveCountGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task ResolveWildcardsAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var config = new BuildConfiguration
        {
            Items = new List<BundleItem>
            {
                new() { Name = "TestItem", Files = new List<BundleFile>() }
            }
        };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await _service.ResolveWildcardsAsync(config, cts.Token));
    }

    [Fact]
    public async Task LoadConfigurationAsync_WithCaseInsensitiveProperties_ParsesCorrectly()
    {
        // Arrange
        var configPath = Path.Combine(_tempDirectory, "config.json");
        var json = @"{
            ""ITEMS"": [
                { ""NAME"": ""TestItem"", ""FILES"": [] }
            ],
            ""PACKS"": {}
        }";
        await File.WriteAllTextAsync(configPath, json);

        // Act
        var result = await _service.LoadConfigurationAsync(configPath);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);
        result.Items[0].Name.Should().Be("TestItem");
    }

    [Fact]
    public async Task LoadProjectConfigurationAsync_WithGeneratedProjectStructure_LoadsItemsPacksAndResolvesWildcards()
    {
        // Arrange
        var projectDir = Path.Combine(_tempDirectory, "MyModProject");
        Directory.CreateDirectory(projectDir);
        var projectPath = Path.Combine(projectDir, "MyModProject.mbproj");

        var generator = new ProjectStructureGenerator(Mock.Of<ILogger<ProjectStructureGenerator>>());
        await generator.GenerateProjectStructureAsync(projectPath, CancellationToken.None);

        // Create sample texture and ini files inside GameFilesEdited
        var textureFile = Path.Combine(projectDir, "GameFilesEdited", "Art", "Textures", "test_texture.tga");
        var iniFile = Path.Combine(projectDir, "GameFilesEdited", "Data", "INI", "test_rules.ini");
        await File.WriteAllTextAsync(textureFile, "dummy tga content");
        await File.WriteAllTextAsync(iniFile, "dummy ini content");

        // Act
        var loadedConfig = await _service.LoadProjectConfigurationAsync(projectPath);

        // Assert
        loadedConfig.Should().NotBeNull();
        loadedConfig!.Items.Should().HaveCount(2);
        loadedConfig.Packs.Should().HaveCount(1);

        var pack = loadedConfig.Packs[0];
        pack.Name.Should().Be("MyMod");
        pack.AllowBuild.Should().BeTrue();
        pack.AllowInstall.Should().BeTrue();
        pack.ItemNames.Should().Contain(new[] { "MyTextures", "MyINI" });

        var texturesItem = loadedConfig.Items.FirstOrDefault(i => i.Name == "MyTextures");
        texturesItem.Should().NotBeNull();
        texturesItem!.Files.Should().Contain(f => Path.GetFullPath(f.AbsSourceFile) == Path.GetFullPath(textureFile));

        var iniItem = loadedConfig.Items.FirstOrDefault(i => i.Name == "MyINI");
        iniItem.Should().NotBeNull();
        iniItem!.Files.Should().Contain(f => Path.GetFullPath(f.AbsSourceFile) == Path.GetFullPath(iniFile));
    }
}
