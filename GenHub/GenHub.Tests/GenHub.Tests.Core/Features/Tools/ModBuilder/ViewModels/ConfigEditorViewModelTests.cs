// <copyright file="ConfigEditorViewModelTests.cs" company="Enowx Labs">
// Copyright (c) Enowx Labs. All rights reserved.
// </copyright>

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.ViewModels;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Resources;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.ModBuilder;
using GenHub.Features.Tools.ModBuilder.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for <see cref="ConfigEditorViewModel"/>.
/// </summary>
public class ConfigEditorViewModelTests
{
    private readonly Mock<IConfigurationLoaderService> _mockConfigLoader;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly Mock<ILogger<ConfigEditorViewModel>> _mockLogger;

    public ConfigEditorViewModelTests()
    {
        _mockConfigLoader = new Mock<IConfigurationLoaderService>();
        _mockNotificationService = new Mock<INotificationService>();
        _mockLogger = new Mock<ILogger<ConfigEditorViewModel>>();
    }

    private ConfigEditorViewModel CreateViewModel() => new(
        _mockConfigLoader.Object,
        _mockNotificationService.Object,
        CreateLocalizationService(),
        _mockLogger.Object);

    private static ILocalizationService CreateLocalizationService()
    {
        var resourceManager = new ResourceManager(LocalizationConstants.StringResourceBaseName, typeof(GenHub.Common.Services.LocalizationService).Assembly);
        var mock = new Mock<ILocalizationService>();
        mock.Setup(m => m.GetString(It.IsAny<string>(), It.IsAny<object?[]>()))
            .Returns<string, object?[]>((key, args) =>
            {
                var val = resourceManager.GetString(key, CultureInfo.InvariantCulture) ?? key;
                return args != null && args.Length > 0 ? string.Format(CultureInfo.InvariantCulture, val, args) : val;
            });
        return mock.Object;
    }

    [Fact]
    public async Task InitializeAsync_PopulatesBundleItemsAndPacksFromProjectAsync()
    {
        var project = new ModBuilderProject
        {
            Name = "TestMod",
            Configuration = new BuildConfiguration
            {
                Items =
                [
                    new BundleItem
                    {
                        Name = "CoreINI",
                        IsBig = true,
                        Files = [new BundleFile { AbsSourceFile = "/test/GameData.ini", RelTargetFile = "INI/GameData.ini" }],
                    }
                ],
                Packs =
                [
                    new BundlePack
                    {
                        Name = "ReleasePack",
                        ItemNames = ["CoreINI"],
                        AllowBuild = true,
                        AllowInstall = true,
                    }
                ],
            },
        };

        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync(project);

        Assert.Single(viewModel.BundleItems);
        Assert.Equal("CoreINI", viewModel.BundleItems[0].Name);
        Assert.True(viewModel.BundleItems[0].IsBig);

        Assert.Single(viewModel.BundlePacks);
        Assert.Equal("ReleasePack", viewModel.BundlePacks[0].Name);
        Assert.Contains("CoreINI", viewModel.BundlePacks[0].ItemNames);
        Assert.False(viewModel.HasChanges);
        Assert.False(viewModel.IsLoading);
    }

    [Fact]
    public async Task AddAndRemoveBundleItem_UpdatesCollectionAndFlagsChangesAsync()
    {
        var project = new ModBuilderProject
        {
            Name = "TestMod",
            Configuration = new BuildConfiguration(),
        };

        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync(project);

        viewModel.AddBundleItemCommand.Execute(null);

        Assert.Single(viewModel.BundleItems);
        Assert.True(viewModel.HasChanges);
        Assert.NotNull(viewModel.SelectedBundleItem);

        viewModel.RemoveBundleItemCommand.Execute(null);

        Assert.Empty(viewModel.BundleItems);
        Assert.Null(viewModel.SelectedBundleItem);
    }

    [Fact]
    public async Task SaveAsync_PreservesExistingFilesAndEventsWithoutDataLossAsync()
    {
        var existingFile = new BundleFile { AbsSourceFile = "/data/GameData.ini", RelTargetFile = "Data/INI/GameData.ini" };
        var existingEvent = new BundleEvent { Type = BundleEventType.OnPreBuild, AbsScript = "tools/patch.py" };

        var project = new ModBuilderProject
        {
            Name = "TestMod",
            Configuration = new BuildConfiguration
            {
                Items =
                [
                    new BundleItem
                    {
                        Name = "CoreData",
                        IsBig = true,
                        Files = [existingFile],
                        Events = new Dictionary<BundleEventType, BundleEvent>
                        {
                            { BundleEventType.OnPreBuild, existingEvent },
                        },
                    }
                ],
            },
        };

        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync(project);

        // Edit name suffix
        viewModel.BundleItems[0].NameSuffix = "_v1";

        // Save
        viewModel.SaveCommand.Execute(null);

        Assert.Single(project.Configuration.Items);
        var savedItem = project.Configuration.Items[0];
        Assert.Equal("CoreData", savedItem.Name);
        Assert.Equal("_v1", savedItem.NameSuffix);
        Assert.Single(savedItem.Files);
        Assert.Equal(existingFile.AbsSourceFile, savedItem.Files[0].AbsSourceFile);
        Assert.True(savedItem.Events.ContainsKey(BundleEventType.OnPreBuild));
        Assert.False(viewModel.HasChanges);
    }

    [Fact]
    public async Task SaveAsync_PreservesPatternsParamsAndManifestsAsync()
    {
        var projectDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(projectDir);
        try
        {
            var resolvedSource = Path.Combine(projectDir, "GameFilesEdited", "Data", "a.ini");
            var project = new ModBuilderProject
            {
                Name = "TestMod",
                ProjectDir = projectDir,
                Configuration = new BuildConfiguration
                {
                    Items =
                    [
                        new BundleItem
                        {
                            Name = "CoreINI",
                            IsBig = true,
                            ManifestFile = "config/patch.big.manifest.json",
                            Description = "Core files",
                            SourcePatterns = ["GameFilesEdited/Data/*.ini"],
                            Files =
                            [
                                new BundleFile
                                {
                                    AbsSourceParent = projectDir,
                                    AbsSourceFile = resolvedSource,
                                    RelTargetFile = "Data/a.ini",
                                    Params = new Dictionary<string, object>
                                    {
                                        { "noconvert", "true" },
                                        { "outputformat", "RAW" },
                                    },
                                },
                            ],
                        },
                    ],
                    Packs =
                    [
                        new BundlePack
                        {
                            Name = "Patch",
                            ItemNames = ["CoreINI"],
                            AllowBuild = true,
                            AllowInstall = true,
                            ManifestFile = "config/patch.big.manifest.json",
                            Description = "Patch release",
                        },
                    ],
                },
            };

            var viewModel = CreateViewModel();

            await viewModel.InitializeAsync(project);

            // Editor shows configured patterns, never resolved absolute paths.
            Assert.Equal("GameFilesEdited/Data/*.ini", viewModel.BundleItems[0].SourcePattern);

            await viewModel.SaveCommand.ExecuteAsync(null);

            var savedItem = Assert.Single(project.Configuration.Items);
            Assert.Equal("GameFilesEdited/Data/*.ini", Assert.Single(savedItem.SourcePatterns));
            Assert.Equal("GameFilesEdited/Data/*.ini", Assert.Single(savedItem.Files).AbsSourceFile);
            Assert.Equal("config/patch.big.manifest.json", savedItem.ManifestFile);
            Assert.Equal("Core files", savedItem.Description);
            var savedParams = savedItem.Files[0].Params;
            Assert.NotNull(savedParams);
            Assert.True(savedParams!.ContainsKey("noconvert"));

            var savedPack = Assert.Single(project.Configuration.Packs);
            Assert.Equal("config/patch.big.manifest.json", savedPack.ManifestFile);
            Assert.Equal("Patch release", savedPack.Description);

            var itemsJson = await File.ReadAllTextAsync(Path.Combine(projectDir, "config", "ModBundleItems.json"));
            Assert.Contains("GameFilesEdited/Data/*.ini", itemsJson, StringComparison.Ordinal);
            Assert.False(ModBuilderViewModel.ContainsAbsoluteSourcePaths(itemsJson));
            Assert.Contains("patch.big.manifest.json", itemsJson, StringComparison.Ordinal);
            Assert.Contains("RAW", itemsJson, StringComparison.Ordinal);

            var packsJson = await File.ReadAllTextAsync(Path.Combine(projectDir, "config", "ModBundlePacks.json"));
            Assert.Contains("patch.big.manifest.json", packsJson, StringComparison.Ordinal);
            Assert.Contains("Patch release", packsJson, StringComparison.Ordinal);

            // Atomic saves leave no temp-file debris behind.
            Assert.Empty(Directory.GetFiles(Path.Combine(projectDir, "config"), "*.tmp"));

            Assert.False(viewModel.HasChanges);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public async Task InitializeAsync_WithPacksAndNoManifests_CreatesDefaultManifestFromProjectInfoAsync()
    {
        var project = new ModBuilderProject
        {
            Name = "TestMod",
            Version = "2.1.0",
            Description = "Project description",
            ContentType = ContentType.Patch,
            TargetGame = GameType.Generals,
            Configuration = new BuildConfiguration
            {
                Packs =
                [
                    new BundlePack { Name = "PackA", ItemNames = ["ItemA"] },
                    new BundlePack { Name = "PackB", ItemNames = ["ItemB"] },
                ],
            },
        };

        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync(project);

        var manifest = Assert.Single(viewModel.BundleManifests);
        Assert.Equal("TestMod", manifest.Name);
        Assert.Equal("2.1.0", manifest.Version);
        Assert.Equal("Project description", manifest.Description);
        Assert.Equal(ContentType.Patch, manifest.ContentType);
        Assert.Equal(GameType.Generals, manifest.TargetGame);
        Assert.Equal(["PackA", "PackB"], manifest.PackNames);
        Assert.Same(manifest, viewModel.SelectedBundleManifest);
        Assert.False(viewModel.HasChanges);
    }

    [Fact]
    public async Task AddBundleManifest_CopiesProjectInfoForVariantAsync()
    {
        var project = new ModBuilderProject
        {
            Name = "TestMod",
            Version = "2.1.0",
            ContentType = ContentType.Patch,
            TargetGame = GameType.Generals,
            Configuration = new BuildConfiguration
            {
                Packs = [new BundlePack { Name = "PackA", ItemNames = ["ItemA"] }],
            },
        };

        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync(project);
        viewModel.AddBundleManifestCommand.Execute(null);

        Assert.Equal(2, viewModel.BundleManifests.Count);
        var variant = viewModel.BundleManifests[1];
        Assert.Equal("NewManifest2", variant.Name);
        Assert.Equal("2.1.0", variant.Version);
        Assert.Equal(ContentType.Patch, variant.ContentType);
        Assert.Equal(GameType.Generals, variant.TargetGame);
        Assert.Empty(variant.PackNames);
        Assert.True(viewModel.HasChanges);
    }

    [Fact]
    public async Task SaveAsync_PersistsDefaultManifestWithProjectInfoAsync()
    {
        var projectDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(projectDir);
        try
        {
            var project = new ModBuilderProject
            {
                Name = "TestMod",
                Version = "2.1.0",
                Description = "Project description",
                ContentType = ContentType.Patch,
                TargetGame = GameType.Generals,
                ProjectDir = projectDir,
                Configuration = new BuildConfiguration
                {
                    Packs = [new BundlePack { Name = "PackA", ItemNames = ["ItemA"] }],
                },
            };

            var viewModel = CreateViewModel();

            await viewModel.InitializeAsync(project);
            await viewModel.SaveCommand.ExecuteAsync(null);

            var savedManifest = Assert.Single(project.Configuration.Manifests);
            Assert.Equal("TestMod", savedManifest.Name);
            Assert.Equal("2.1.0", savedManifest.Version);
            Assert.Equal(ContentType.Patch, savedManifest.ContentType);
            Assert.Equal(GameType.Generals, savedManifest.TargetGame);
            Assert.Equal(["PackA"], savedManifest.PackNames);

            var manifestsJson = await File.ReadAllTextAsync(Path.Combine(projectDir, "config", "ModBundleManifests.json"));
            Assert.Contains("\"ContentType\": \"Patch\"", manifestsJson, StringComparison.Ordinal);
            Assert.Contains("\"TargetGame\": \"Generals\"", manifestsJson, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_DeduplicatesBundleItemsAndPacksByNameCaseInsensitively()
    {
        var projectDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(projectDir);
        try
        {
            var project = new ModBuilderProject
            {
                Name = "DedupTest",
                ProjectDir = projectDir,
                Configuration = new BuildConfiguration
                {
                    Items =
                    [
                        new BundleItem { Name = "ItemA" },
                        new BundleItem { Name = "itema" },
                    ],
                    Packs =
                    [
                        new BundlePack { Name = "PackA" },
                        new BundlePack { Name = "PACKA" },
                    ],
                },
            };

            var viewModel = CreateViewModel();
            await viewModel.InitializeAsync(project);

            var locMock = new Mock<ILocalizationService>();
            viewModel.BundleItems.Add(new BundleItemEditorViewModel(locMock.Object) { Name = "itema" });
            viewModel.BundlePacks.Add(new BundlePackConfigViewModel { Name = "packa" });

            await viewModel.SaveCommand.ExecuteAsync(null);

            Assert.Single(project.Configuration.Items);
            Assert.Single(project.Configuration.Packs);
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }
}
