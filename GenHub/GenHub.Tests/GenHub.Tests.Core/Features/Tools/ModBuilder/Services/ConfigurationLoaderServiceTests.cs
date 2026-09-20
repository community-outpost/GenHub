using FluentAssertions;
using GenHub.Core.Models.Tools.ModBuilder;
using GenHub.Features.Tools.ModBuilder.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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
    public async Task LoadConfigurationResultAsync_WithValidConfig_ReturnsConfiguration()
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
        var opResult = await _service.LoadConfigurationResultAsync(configPath);
        opResult.Success.Should().BeTrue();
        var result = opResult.Data!;

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);
        result.Items[0].Name.Should().Be("TestItem");
        result.Packs.Should().Contain(pack => pack.Name == "TestPack");
        result.LoadedConfigFiles.Should().Contain(configPath);
    }

    [Fact]
    public async Task LoadConfigurationResultAsync_WithNonExistentFile_ReturnsFailure()
    {
        // Arrange
        var configPath = Path.Combine(_tempDirectory, "nonexistent.json");

        // Act
        var result = await _service.LoadConfigurationResultAsync(configPath);

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task LoadConfigurationResultAsync_WithInvalidJson_ReturnsFailure()
    {
        // Arrange
        var configPath = Path.Combine(_tempDirectory, "invalid.json");
        await File.WriteAllTextAsync(configPath, "{ invalid json }");

        // Act
        var result = await _service.LoadConfigurationResultAsync(configPath);

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task LoadConfigurationResultAsync_WithEmptyFile_ReturnsFailure()
    {
        // Arrange
        var configPath = Path.Combine(_tempDirectory, "empty.json");
        await File.WriteAllTextAsync(configPath, string.Empty);

        // Act
        var result = await _service.LoadConfigurationResultAsync(configPath);

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task LoadConfigurationResultAsync_WithComments_IgnoresComments()
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
        var result = (await _service.LoadConfigurationResultAsync(configPath)).Data!;

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().BeEmpty();
        result.Packs.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadConfigurationResultAsync_WithTrailingCommas_HandlesCorrectly()
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
        var result = (await _service.LoadConfigurationResultAsync(configPath)).Data!;

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task LoadAndMergeConfigurationsResultAsync_WithEmptyList_ReturnsEmptyConfiguration()
    {
        // Arrange
        var configPaths = new List<string>();

        // Act
        var result = (await _service.LoadAndMergeConfigurationsResultAsync(configPaths)).Data!;

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().BeEmpty();
        result.Packs.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAndMergeConfigurationsResultAsync_WithSingleConfig_ReturnsSameConfig()
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
        var result = (await _service.LoadAndMergeConfigurationsResultAsync(new[] { configPath })).Data!;

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);
        result.Items[0].Name.Should().Be("TestItem");
    }

    [Fact]
    public async Task LoadAndMergeConfigurationsResultAsync_WithMultipleConfigs_MergesCorrectly()
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
        var result = (await _service.LoadAndMergeConfigurationsResultAsync(new[] { config1Path, config2Path })).Data!;

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
    public async Task ResolveWildcardsAsync_WithRelativeExplicitEntry_AnchorsToProjectDirectory()
    {
        // Arrange
        var editedDir = Path.Combine(_tempDirectory, "GameFilesEdited");
        Directory.CreateDirectory(editedDir);
        var testFile = Path.Combine(editedDir, "ControlBarPro.txt");
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
                            AbsSourceFile = "GameFilesEdited/ControlBarPro.txt",
                            RelTargetFile = "ControlBarPro.txt"
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
        var resolved = result.Items[0].Files[0].AbsSourceFile;
        Path.IsPathRooted(resolved).Should().BeTrue();
        File.Exists(resolved).Should().BeTrue();
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
    public async Task LoadConfigurationResultAsync_WithCaseInsensitiveProperties_ParsesCorrectly()
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
        var result = (await _service.LoadConfigurationResultAsync(configPath)).Data!;

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
        loadedConfig!.Items.Should().HaveCount(4);
        loadedConfig.Packs.Should().HaveCount(2);

        var pack = loadedConfig.Packs.FirstOrDefault(p => p.Name == "CommunityDataPatch");
        pack.Should().NotBeNull();
        pack!.AllowBuild.Should().BeTrue();
        pack.AllowInstall.Should().BeTrue();
        pack.ItemNames.Should().Contain(new[] { "CoreINIPatch", "CoreTextures", "CoreAudio", "GameScripts" });

        var texturesItem = loadedConfig.Items.FirstOrDefault(i => i.Name == "CoreTextures");
        texturesItem.Should().NotBeNull();
        texturesItem!.Files.Should().Contain(f => Path.GetFullPath(f.AbsSourceFile) == Path.GetFullPath(textureFile));

        var iniItem = loadedConfig.Items.FirstOrDefault(i => i.Name == "CoreINIPatch");
        iniItem.Should().NotBeNull();
        iniItem!.Files.Should().Contain(f => Path.GetFullPath(f.AbsSourceFile) == Path.GetFullPath(iniFile));
    }

    [Fact]
    public async Task LoadConfigurationResultAsync_WithPythonBundlePackBig_MapsBigAndOutputFile()
    {
        // Arrange
        var configPath = Path.Combine(_tempDirectory, "bundle_packs.json");
        var json = @"{
            ""bundles"": {
                ""packs"": [
                    {
                        ""name"": ""CommunityPatchCoreINI"",
                        ""big"": true,
                        ""outputFile"": ""500_900_CommunityPatch_CoreINI.big"",
                        ""itemNames"": [""CoreINIPatch""]
                    }
                ]
            }
        }";
        await File.WriteAllTextAsync(configPath, json);

        // Act
        var result = (await _service.LoadConfigurationResultAsync(configPath)).Data!;

        // Assert
        result.Should().NotBeNull();
        result.Packs.Should().HaveCount(1);
        var pack = result.Packs[0];
        pack.Name.Should().Be("CommunityPatchCoreINI");
        pack.Big.Should().BeTrue();
        pack.OutputFile.Should().Be("500_900_CommunityPatch_CoreINI.big");
        pack.IsBigPack.Should().BeTrue();
    }

    [Fact]
    public async Task LoadConfigurationResultAsync_WithSimplifiedBundlePackBig_MapsBig()
    {
        // Arrange
        var configPath = Path.Combine(_tempDirectory, "config.json");
        var json = @"{
            ""BundlePacks"": [
                {
                    ""Name"": ""SimplifiedPatch"",
                    ""Big"": true,
                    ""Items"": [""CoreINIPatch""]
                }
            ]
        }";
        await File.WriteAllTextAsync(configPath, json);

        // Act
        var result = (await _service.LoadConfigurationResultAsync(configPath)).Data!;

        // Assert
        result.Should().NotBeNull();
        result.Packs.Should().HaveCount(1);
        var pack = result.Packs[0];
        pack.Name.Should().Be("SimplifiedPatch");
        pack.Big.Should().BeTrue();
        pack.IsBigPack.Should().BeTrue();
    }

    [Fact]
    public async Task LoadConfigurationResultAsync_WithSimplifiedBundlePackBigFalseAndBigOutputFile_MapsBigFalse()
    {
        // Arrange
        var configPath = Path.Combine(_tempDirectory, "config.json");
        var json = @"{
            ""BundlePacks"": [
                {
                    ""Name"": ""SimplifiedPatch"",
                    ""Big"": false,
                    ""OutputFile"": ""500_900_CommunityPatch_CoreINI.big"",
                    ""Items"": [""CoreINIPatch""]
                }
            ]
        }";
        await File.WriteAllTextAsync(configPath, json);

        // Act
        var result = (await _service.LoadConfigurationResultAsync(configPath)).Data!;

        // Assert
        result.Should().NotBeNull();
        result.Packs.Should().HaveCount(1);
        var pack = result.Packs[0];
        pack.Name.Should().Be("SimplifiedPatch");
        pack.Big.Should().BeFalse();
        pack.IsBigPack.Should().BeFalse();
    }

    [Fact]
    public async Task LoadConfigurationResultAsync_WithSimplifiedBundleItemManifestFile_MapsManifest()
    {
        // Arrange
        var configPath = Path.Combine(_tempDirectory, "items.json");
        var json = @"{
            ""BundleItems"": [
                {
                    ""Name"": ""PatchINI"",
                    ""SourceFiles"": [""GameFilesEdited/Data/INI/**/*.ini""],
                    ""OutputFormat"": ""INI"",
                    ""ManifestFile"": ""config/500_900_CommunityPatch_CoreINI.big.manifest.json""
                }
            ]
        }";
        await File.WriteAllTextAsync(configPath, json);

        // Act
        var result = (await _service.LoadConfigurationResultAsync(configPath)).Data!;

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);
        result.Items[0].ManifestFile.Should().Be("config/500_900_CommunityPatch_CoreINI.big.manifest.json");
    }

    [Fact]
    public async Task LoadConfigurationResultAsync_WithSimplifiedExplicitFile_StripsGameFilesEditedPrefix()
    {
        // Arrange
        var configPath = Path.Combine(_tempDirectory, "items.json");
        var json = @"{
            ""BundleItems"": [
                {
                    ""Name"": ""Base"",
                    ""SourceFiles"": [""GameFilesEdited/ControlBarPro.txt""],
                    ""OutputFormat"": ""RAW"",
                    ""NoConvert"": true
                }
            ]
        }";
        await File.WriteAllTextAsync(configPath, json);

        // Act
        var result = (await _service.LoadConfigurationResultAsync(configPath)).Data!;

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);
        result.Items[0].Files.Should().HaveCount(1);
        result.Items[0].Files[0].RelTargetFile.Should().Be("ControlBarPro.txt");
    }

    [Fact]
    public async Task LoadConfigurationResultAsync_WithPythonBundleItemManifestFile_MapsManifest()
    {
        // Arrange
        var configPath = Path.Combine(_tempDirectory, "bundle_items.json");
        var json = @"{
            ""bundles"": {
                ""items"": [
                    {
                        ""name"": ""PatchINI"",
                        ""manifestFile"": ""config/500_900_CommunityPatch_CoreINI.big.manifest.json"",
                        ""files"": []
                    }
                ]
            }
        }";
        await File.WriteAllTextAsync(configPath, json);

        // Act
        var result = (await _service.LoadConfigurationResultAsync(configPath)).Data!;

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);
        result.Items[0].ManifestFile.Should().Be("config/500_900_CommunityPatch_CoreINI.big.manifest.json");
    }

    [Fact]
    public async Task LoadConfigurationResultAsync_WithSimplifiedMetadata_MapsAllFields()
    {
        // Arrange
        var configDir = Path.Combine(_tempDirectory, "config");
        Directory.CreateDirectory(configDir);
        var configPath = Path.Combine(configDir, "ModBundleItems.json");
        var json = """
        {
            "BundleItems": [
                {
                    "Name": "PatchINI",
                    "NamePrefix": "Pre_",
                    "NameSuffix": "_Suf",
                    "SetGameLanguageOnInstall": "English",
                    "TargetDir": "Data",
                    "BaseDir": "GameFilesEdited",
                    "BigSuffix": ".v2",
                    "SourceFiles": [ "GameFilesEdited/Data/INI/**/*.ini" ],
                    "OutputFormat": "INI",
                    "Description": "Patch INI files"
                }
            ],
            "BundlePacks": [
                {
                    "Name": "Patch",
                    "NamePrefix": "PackPre_",
                    "NameSuffix": "_PackSuf",
                    "SetGameLanguageOnInstall": "English",
                    "Items": [ "PatchINI" ],
                    "OutputFile": ".Release/patch.big",
                    "ManifestFile": "config/patch.big.manifest.json",
                    "Description": "Patch release"
                }
            ]
        }
        """;
        await File.WriteAllTextAsync(configPath, json);

        // Act
        var result = (await _service.LoadConfigurationResultAsync(configPath)).Data!;

        // Assert
        var item = result.Items.Should().ContainSingle().Subject;
        item.NamePrefix.Should().Be("Pre_");
        item.NameSuffix.Should().Be("_Suf");
        item.SetGameLanguageOnInstall.Should().Be("English");
        item.TargetDir.Should().Be("Data");
        item.BaseDir.Should().Be("GameFilesEdited");
        item.BigSuffix.Should().Be(".v2");
        item.SourcePatterns.Should().ContainSingle().Which.Should().Be("GameFilesEdited/Data/INI/**/*.ini");

        var pack = result.Packs.Should().ContainSingle().Subject;
        pack.NamePrefix.Should().Be("PackPre_");
        pack.NameSuffix.Should().Be("_PackSuf");
        pack.SetGameLanguageOnInstall.Should().Be("English");
        pack.ManifestFile.Should().Be("config/patch.big.manifest.json");
        pack.Description.Should().Be("Patch release");
    }

    [Fact]
    public async Task ResolveWildcardsAsync_SnapshotsSourcePatternsBeforeResolution()
    {
        // Arrange
        var gameFilesDir = Path.Combine(_tempDirectory, "GameFilesEdited", "Data");
        Directory.CreateDirectory(gameFilesDir);
        await File.WriteAllTextAsync(Path.Combine(gameFilesDir, "a.ini"), "a");
        await File.WriteAllTextAsync(Path.Combine(gameFilesDir, "b.ini"), "b");

        var configuration = new BuildConfiguration
        {
            Items = new List<BundleItem>
            {
                new()
                {
                    Name = "PatchINI",
                    Files = new List<BundleFile>
                    {
                        new() { AbsSourceParent = _tempDirectory, AbsSourceFile = "GameFilesEdited/Data/*.ini", RelTargetFile = string.Empty },
                    },
                },
            },
        };

        // Act
        var result = await _service.ResolveWildcardsAsync(configuration);

        // Assert
        var item = result.Items.Should().ContainSingle().Subject;
        item.Files.Should().HaveCount(2);
        item.SourcePatterns.Should().ContainSingle().Which.Should().Be("GameFilesEdited/Data/*.ini");
    }

    [Fact]
    public async Task LoadConfigurationResultAsync_WithAbsoluteExplicitEntry_RelativizesTarget()
    {
        // Arrange: entries corrupted by older editor saves persist resolved
        // absolute paths; targets must still resolve to relative paths.
        var iniDir = Path.Combine(_tempDirectory, "GameFilesEdited", "Data", "INI");
        Directory.CreateDirectory(iniDir);
        var absoluteSource = Path.Combine(iniDir, "GameData.ini");
        await File.WriteAllTextAsync(absoluteSource, "data");
        var escapedSource = absoluteSource.Replace("\\", "\\\\");

        var configDir = Path.Combine(_tempDirectory, "config");
        Directory.CreateDirectory(configDir);
        var configPath = Path.Combine(configDir, "ModBundleItems.json");
        var json = "{ \"BundleItems\": [ { \"Name\": \"PatchINI\", \"SourceFiles\": [ \"" + escapedSource + "\" ] } ] }";
        await File.WriteAllTextAsync(configPath, json);

        // Act
        var result = (await _service.LoadConfigurationResultAsync(configPath)).Data!;

        // Assert
        var file = result.Items.Should().ContainSingle().Subject.Files.Should().ContainSingle().Subject;
        Path.IsPathRooted(file.RelTargetFile).Should().BeFalse();
        file.RelTargetFile.Replace('\\', '/').Should().Be("Data/INI/GameData.ini");
    }

    [Fact]
    public void RelativizeToProject_WithAbsoluteInsideProject_ReturnsRelative()
    {
        // Arrange
        var projectDir = Path.Combine(_tempDirectory, "Proj");
        var absolute = Path.Combine(projectDir, "GameFilesEdited", "a.ini");

        // Act
        var result = ConfigurationLoaderService.RelativizeToProject(absolute, projectDir);

        // Assert
        Path.IsPathRooted(result).Should().BeFalse();
        result.Should().Be(Path.Combine("GameFilesEdited", "a.ini"));
    }

    [Fact]
    public void RelativizeToProject_WithAbsoluteOutsideProject_StripsRoot()
    {
        // Arrange
        var projectDir = Path.Combine(_tempDirectory, "Proj");
        var outside = Path.Combine(Path.GetPathRoot(_tempDirectory)!, "elsewhere", "a.ini");

        // Act
        var result = ConfigurationLoaderService.RelativizeToProject(outside, projectDir);

        // Assert
        Path.IsPathRooted(result).Should().BeFalse();
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void RelativizeToProject_WithRelativePattern_ReturnsUnchanged()
    {
        // Act
        var result = ConfigurationLoaderService.RelativizeToProject("GameFilesEdited/Data/*.ini", _tempDirectory);

        // Assert
        result.Should().Be("GameFilesEdited/Data/*.ini");
    }

    [Fact]
    public async Task LoadConfigurationResultAsync_WithSimplifiedBundleManifests_MapsManifests()
    {
        // Arrange
        var configPath = Path.Combine(_tempDirectory, "ModBundleManifests.json");
        var json = @"{
            ""BundleManifests"": [
                {
                    ""Name"": ""HdEdition"",
                    ""Version"": ""2.5.0"",
                    ""Publisher"": ""acme"",
                    ""Description"": ""Groups the HD packs"",
                    ""Packs"": [""PackA"", ""PackB""]
                }
            ]
        }";
        await File.WriteAllTextAsync(configPath, json);

        // Act
        var result = (await _service.LoadConfigurationResultAsync(configPath)).Data!;

        // Assert
        result.Should().NotBeNull();
        result.Manifests.Should().HaveCount(1);
        var manifest = result.Manifests[0];
        manifest.Name.Should().Be("HdEdition");
        manifest.Version.Should().Be("2.5.0");
        manifest.Publisher.Should().Be("acme");
        manifest.Description.Should().Be("Groups the HD packs");
        manifest.PackNames.Should().ContainInOrder("PackA", "PackB");
    }

    [Fact]
    public void MergeConfigurations_WithManifests_MergesAndSkipsDuplicates()
    {
        // Arrange
        var baseConfig = new BuildConfiguration
        {
            Manifests =
            [
                new() { Name = "Edition", Version = "1.0.0", PackNames = ["PackA"] },
            ],
        };
        var overrideConfig = new BuildConfiguration
        {
            Manifests =
            [
                new() { Name = "Edition", Version = "2.0.0", PackNames = ["PackB"] },
                new() { Name = "Extra", PackNames = ["PackB"] },
            ],
        };

        // Act
        var merged = _service.MergeConfigurations(baseConfig, overrideConfig);

        // Assert
        merged.Manifests.Should().HaveCount(2);
        merged.Manifests.Select(m => m.Name).Should().ContainInOrder("Edition", "Extra");
        merged.Manifests[0].Version.Should().Be("1.0.0");
    }

    [Fact]
    public void ValidateConfiguration_WithManifestReferencingUnknownPack_ReturnsError()
    {
        // Arrange
        var configuration = new BuildConfiguration
        {
            Items =
            [
                new() { Name = "ItemA", Files = [new BundleFile()] },
            ],
            Packs =
            [
                new() { Name = "PackA", ItemNames = ["ItemA"] },
            ],
            Manifests =
            [
                new() { Name = "Broken", PackNames = ["PackA", "Nope"] },
            ],
        };

        // Act
        var errors = _service.ValidateConfiguration(configuration);

        // Assert
        errors.Should().ContainSingle(e => e.Contains("Broken", StringComparison.Ordinal) && e.Contains("Nope", StringComparison.Ordinal));
    }
}
