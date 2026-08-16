// <copyright file="ConfigEditorViewModelTests.cs" company="Enowx Labs">
// Copyright (c) Enowx Labs. All rights reserved.
// </copyright>

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.ViewModels;

using System.Collections.Generic;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.ModBuilder;
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

    [Fact]
    public async Task InitializeAsync_PopulatesBundleItemsAndPacksFromProject()
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

        var viewModel = new ConfigEditorViewModel(
            _mockConfigLoader.Object,
            _mockNotificationService.Object,
            _mockLogger.Object);

        await viewModel.InitializeAsync(project);

        Assert.Single(viewModel.BundleItems);
        Assert.Equal("CoreINI", viewModel.BundleItems[0].Name);
        Assert.True(viewModel.BundleItems[0].IsBig);

        Assert.Single(viewModel.BundlePacks);
        Assert.Equal("ReleasePack", viewModel.BundlePacks[0].Name);
        Assert.Contains("CoreINI", viewModel.BundlePacks[0].ItemNames);
        Assert.False(viewModel.HasChanges);
    }

    [Fact]
    public async Task AddAndRemoveBundleItem_UpdatesCollectionAndFlagsChanges()
    {
        var project = new ModBuilderProject
        {
            Name = "TestMod",
            Configuration = new BuildConfiguration(),
        };

        var viewModel = new ConfigEditorViewModel(
            _mockConfigLoader.Object,
            _mockNotificationService.Object,
            _mockLogger.Object);

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
    public async Task SaveAsync_PreservesExistingFilesAndEventsWithoutDataLoss()
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

        var viewModel = new ConfigEditorViewModel(
            _mockConfigLoader.Object,
            _mockNotificationService.Object,
            _mockLogger.Object);

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
}
