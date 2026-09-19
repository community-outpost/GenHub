// <copyright file="ModBuilderViewModelTests.cs" company="Enowx Labs">
// Copyright (c) Enowx Labs. All rights reserved.
// </copyright>

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.ViewModels;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Results.ModBuilder;
using GenHub.Core.Models.Tools.ModBuilder;
using GenHub.Core.Models.Enums;
using GenHub.Features.Tools.ModBuilder.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for <see cref="ModBuilderViewModel"/>.
/// </summary>
public class ModBuilderViewModelTests : IDisposable
{
    private readonly Mock<IBuildEngineService> _mockBuildEngine;
    private readonly Mock<IProjectConfigService> _mockProjectConfigService;
    private readonly Mock<IConfigurationLoaderService> _mockConfigLoader;
    private readonly Mock<IProjectStructureGenerator> _mockProjectStructureGenerator;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly Mock<IGameInstallationService> _mockGameInstallService;
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly Mock<ILogger<ModBuilderViewModel>> _mockLogger;
    private readonly Mock<ILogger<FileManagerViewModel>> _mockFileManagerLogger;
    private readonly FileManagerViewModel _fileManager;
    private readonly string _tempDir;

    public ModBuilderViewModelTests()
    {
        _mockBuildEngine = new Mock<IBuildEngineService>();
        _mockProjectConfigService = new Mock<IProjectConfigService>();
        _mockConfigLoader = new Mock<IConfigurationLoaderService>();
        _mockProjectStructureGenerator = new Mock<IProjectStructureGenerator>();
        _mockNotificationService = new Mock<INotificationService>();
        _mockGameInstallService = new Mock<IGameInstallationService>();
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockLogger = new Mock<ILogger<ModBuilderViewModel>>();
        _mockFileManagerLogger = new Mock<ILogger<FileManagerViewModel>>();

        _fileManager = new FileManagerViewModel(
            _mockGameInstallService.Object,
            _mockNotificationService.Object,
            _mockFileManagerLogger.Object);

        _mockLoggerFactory
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(_mockLogger.Object);

        _tempDir = Path.Combine(Path.GetTempPath(), "GenHub_ModBuilderVMTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Ignore cleanup failures
        }
    }

    [Fact]
    public void IsPathInsideAppDirectory_ReturnsTrueForBaseDirectoryAndSubpaths()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var subPath = Path.Combine(baseDir, "SampleProjects", "ModBuilder", "Test");

        Assert.True(ModBuilderViewModel.IsPathInsideAppDirectory(baseDir));
        Assert.True(ModBuilderViewModel.IsPathInsideAppDirectory(subPath));
        Assert.False(ModBuilderViewModel.IsPathInsideAppDirectory(Path.GetTempPath()));
    }

    [Fact]
    public void InitialState_IsUnloadedAndReady()
    {
        var viewModel = CreateViewModel();

        Assert.Null(viewModel.CurrentProject);
        Assert.False(viewModel.IsProjectLoaded);
        Assert.Equal("Ready", viewModel.BuildStatus);
        Assert.Empty(viewModel.Bundles);
    }

    [Fact]
    public void PercentComplete_WhenUpdated_NotifiesProgressText()
    {
        var viewModel = CreateViewModel();

        viewModel.PercentComplete = 75.5;

        Assert.Equal(75.5, viewModel.PercentComplete);
        Assert.Equal("75.5%", viewModel.ProgressText);
    }

    [Fact]
    public async Task CloseProject_ResetsProjectStateToDashboard()
    {
        var viewModel = CreateViewModel();

        viewModel.CurrentProject = new ModBuilderProject { Name = "TestMod" };
        viewModel.ProjectPath = @"C:\Test\TestMod.mbproj";
        viewModel.Bundles.Add(new BundleItemViewModel { Name = "Core", IsSelected = true });

        Assert.True(viewModel.IsProjectLoaded);

        await viewModel.CloseProjectCommand.ExecuteAsync(null);

        Assert.Null(viewModel.CurrentProject);
        Assert.Empty(viewModel.ProjectPath);
        Assert.False(viewModel.IsProjectLoaded);
        Assert.Empty(viewModel.Bundles);
    }

    [Fact]
    public async Task OpenRecentProject_WhenFileDoesNotExist_ShowsWarning()
    {
        var viewModel = CreateViewModel();
        var nonExistentPath = Path.Combine(_tempDir, "NonExistent.mbproj");

        await viewModel.OpenRecentProjectCommand.ExecuteAsync(nonExistentPath);

        _mockNotificationService.Verify(
            n => n.ShowWarning(
                It.Is<string>(t => t == "Project Not Found"),
                It.Is<string>(s => s.Contains("NonExistent.mbproj")),
                It.IsAny<int?>(),
                It.IsAny<bool>()),
            Times.Once);
    }

    [Fact]
    public void ClearOutput_ClearsBuildLog()
    {
        var viewModel = CreateViewModel();
        viewModel.BuildLog.Add("Sample build log entry");

        viewModel.ClearOutputCommand.Execute(null);

        Assert.Empty(viewModel.BuildLog);
        Assert.Equal(string.Empty, viewModel.BuildOutput);
    }

    private ModBuilderViewModel CreateViewModel(IDialogService? dialogService = null)
    {
        return new ModBuilderViewModel(
            _mockBuildEngine.Object,
            _mockProjectConfigService.Object,
            _mockConfigLoader.Object,
            _mockProjectStructureGenerator.Object,
            _mockNotificationService.Object,
            CreateLocalizationService(),
            _fileManager,
            _mockLoggerFactory.Object,
            _mockLogger.Object,
            dialogService);
    }

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
        mock.Setup(m => m[It.IsAny<string>()])
            .Returns<string>(key => resourceManager.GetString(key, CultureInfo.InvariantCulture) ?? key);
        return mock.Object;
    }

    /// <summary>
    /// Verifies that CreateManifestEnabled defaults to true.
    /// </summary>
    [Fact]
    public void CreateManifestEnabled_DefaultsToTrue()
    {
        var viewModel = CreateViewModel();

        Assert.True(viewModel.CreateManifestEnabled);
    }

    /// <summary>
    /// Verifies that CreateManifestCommand invokes the build engine with BuildStep.CreateManifest.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestCommand_WhenExecuted_InvokesBuildEngineWithCreateManifestStep()
    {
        var viewModel = CreateViewModel();
        var project = new ModBuilderProject
        {
            Name = "TestMod",
            ProjectDir = _tempDir,
        };
        viewModel.CurrentProject = project;
        viewModel.ProjectPath = Path.Combine(_tempDir, "TestMod.mbproj");

        _mockBuildEngine
            .Setup(b => b.ExecuteBuildAsync(
                project,
                It.IsAny<BuildConfiguration>(),
                It.IsAny<List<string>>(),
                BuildStep.CreateManifest,
                It.IsAny<IProgress<BuildProgress>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildOperationResult.CreateSuccess());

        await viewModel.CreateManifestCommand.ExecuteAsync(null);

        _mockBuildEngine.Verify(
            b => b.ExecuteBuildAsync(
                project,
                It.IsAny<BuildConfiguration>(),
                It.IsAny<List<string>>(),
                BuildStep.CreateManifest,
                It.IsAny<IProgress<BuildProgress>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _mockNotificationService.Verify(
            n => n.ShowSuccess(
                "Manifest Created",
                It.Is<string>(s => s.Contains("TestMod")),
                It.IsAny<int?>(),
                It.IsAny<bool>()),
            Times.Once);
    }

    [Fact]
    public void TargetGame_DefaultsToZeroHour()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(GameType.ZeroHour, viewModel.SelectedTargetGame);
        Assert.Contains(GameType.ZeroHour, viewModel.AvailableTargetGames);
        Assert.Contains(GameType.Generals, viewModel.AvailableTargetGames);
    }

    [Fact]
    public void TargetGame_WhenProjectLoaded_SynchronizesWithProject()
    {
        var viewModel = CreateViewModel();
        var project = new ModBuilderProject
        {
            Name = "GeneralsPatch",
            TargetGame = GameType.Generals,
        };

        viewModel.CurrentProject = project;

        Assert.Equal(GameType.Generals, viewModel.SelectedTargetGame);
    }

    [Fact]
    public void TargetGame_WhenChanged_UpdatesProjectTargetGame()
    {
        var viewModel = CreateViewModel();
        var project = new ModBuilderProject
        {
            Name = "TestMod",
            TargetGame = GameType.ZeroHour,
        };

        viewModel.CurrentProject = project;
        viewModel.SelectedTargetGame = GameType.Generals;

        Assert.Equal(GameType.Generals, project.TargetGame);
    }

    /// <summary>
    /// Verifies that SelectedContentType defaults to ContentType.Mod.
    /// </summary>
    [Fact]
    public void ContentType_DefaultsToMod()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(ContentType.Mod, viewModel.SelectedContentType);
        Assert.Contains(ContentType.Mod, viewModel.AvailableContentTypes);
        Assert.Contains(ContentType.Patch, viewModel.AvailableContentTypes);
        Assert.Contains(ContentType.Addon, viewModel.AvailableContentTypes);
    }

    /// <summary>
    /// Verifies that SelectedContentType synchronizes when CurrentProject changes.
    /// </summary>
    [Fact]
    public void ContentType_WhenProjectLoaded_SynchronizesWithProject()
    {
        var viewModel = CreateViewModel();
        var project = new ModBuilderProject
        {
            Name = "GeneralsPatch",
            ContentType = ContentType.Patch,
        };

        viewModel.CurrentProject = project;

        Assert.Equal(ContentType.Patch, viewModel.SelectedContentType);
    }

    /// <summary>
    /// Verifies that changing SelectedContentType updates CurrentProject.ContentType.
    /// </summary>
    [Fact]
    public void ContentType_WhenChanged_UpdatesProjectContentType()
    {
        var viewModel = CreateViewModel();
        var project = new ModBuilderProject
        {
            Name = "TestMod",
            ContentType = ContentType.Mod,
        };

        viewModel.CurrentProject = project;
        viewModel.SelectedContentType = ContentType.Addon;

        Assert.Equal(ContentType.Addon, project.ContentType);
    }

    /// <summary>
    /// Verifies that opening a recent project while a build is running is refused
    /// without touching the project on disk.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task OpenRecentProject_WhenBuildRunning_DoesNotLoadProject()
    {
        var projectPath = Path.Combine(_tempDir, "Guarded.mbproj");
        await File.WriteAllTextAsync(projectPath, "{}");

        var viewModel = CreateViewModel();
        viewModel.IsBuildRunning = true;

        await viewModel.OpenRecentProjectCommand.ExecuteAsync(projectPath);

        Assert.False(viewModel.IsProjectLoaded);
        Assert.Null(viewModel.CurrentProject);
        _mockProjectConfigService.Verify(
            m => m.LoadProjectAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that opening a recent project while idle loads the project
    /// and releases the exclusive build slot afterwards.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task OpenRecentProject_WhenIdle_LoadsProjectAndReleasesBuildSlot()
    {
        var projectDir = Path.Combine(_tempDir, "SlotProject");
        Directory.CreateDirectory(projectDir);
        var projectPath = Path.Combine(projectDir, "SlotProject.mbproj");
        await File.WriteAllTextAsync(projectPath, "{}");

        var project = new ModBuilderProject
        {
            Name = "SlotProject",
            ProjectDir = projectDir,
        };

        _mockProjectConfigService
            .Setup(m => m.LoadProjectAsync(projectPath, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProjectOperationResult<ModBuilderProject>.CreateSuccess(project));
        _mockProjectConfigService
            .Setup(m => m.AddToRecentProjectsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProjectOperationResult<bool>.CreateSuccess(true));
        _mockConfigLoader
            .Setup(m => m.LoadProjectConfigurationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BuildConfiguration());

        var viewModel = CreateViewModel();

        await viewModel.OpenRecentProjectCommand.ExecuteAsync(projectPath);

        Assert.True(viewModel.IsProjectLoaded);
        Assert.Same(project, viewModel.CurrentProject);
        Assert.False(viewModel.IsBuildRunning);
    }

    /// <summary>
    /// Verifies that deleting a project while a build is running is refused
    /// without prompting for confirmation or deleting files.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeleteRecentProject_WhenBuildRunning_DoesNotDeleteProject()
    {
        var projectDir = Path.Combine(_tempDir, "BusyProject");
        Directory.CreateDirectory(projectDir);
        var projectPath = Path.Combine(projectDir, "BusyProject.mbproj");
        await File.WriteAllTextAsync(projectPath, "{}");

        var mockDialogService = new Mock<IDialogService>();
        var viewModel = CreateViewModel(mockDialogService.Object);
        viewModel.IsBuildRunning = true;

        await viewModel.DeleteRecentProjectCommand.ExecuteAsync(projectPath);

        Assert.True(File.Exists(projectPath));
        mockDialogService.Verify(
            d => d.ShowConfirmationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public void ContainsAbsoluteSourcePaths_DetectsWindowsAndPosixAbsolutes()
    {
        Assert.True(ModBuilderViewModel.ContainsAbsoluteSourcePaths("{ \"SourceFiles\": [ \"C:\\\\Users\\\\a\\\\f.ini\" ] }"));
        Assert.True(ModBuilderViewModel.ContainsAbsoluteSourcePaths("{ \"SourceFiles\": [ \"C:/Users/a/f.ini\" ] }"));
        Assert.True(ModBuilderViewModel.ContainsAbsoluteSourcePaths("{ \"SourceFiles\": [ \"/home/a/f.ini\" ] }"));
        Assert.False(ModBuilderViewModel.ContainsAbsoluteSourcePaths("{ \"SourceFiles\": [ \"GameFilesEdited/Data/*.ini\" ] }"));
    }

    [Fact]
    public async Task ShouldUpdateSampleConfigFile_WithCorruptedItemsFile_ReturnsTrueAsync()
    {
        var configDir = Path.Combine(_tempDir, "CorruptSample", "config");
        Directory.CreateDirectory(configDir);
        var itemsFile = Path.Combine(configDir, "ModBundleItems.json");
        await File.WriteAllTextAsync(itemsFile, "{ \"BundleItems\": [ { \"Name\": \"PatchINI\", \"SourceFiles\": [ \"C:\\\\Samples\\\\GeneralsGamePatch2\\\\f.ini\" ] } ] }");

        Assert.True(ModBuilderViewModel.ShouldUpdateSampleConfigFile("GeneralsGamePatch2", itemsFile));
    }

    [Fact]
    public async Task ShouldUpdateSampleConfigFile_WithCurrentItemsFile_ReturnsFalseAsync()
    {
        var configDir = Path.Combine(_tempDir, "CurrentSample", "config");
        Directory.CreateDirectory(configDir);
        var itemsFile = Path.Combine(configDir, "ModBundleItems.json");
        await File.WriteAllTextAsync(itemsFile, "{ \"BundleItems\": [ { \"Name\": \"PatchINI\", \"SourceFiles\": [ \"GameFilesEdited/Data/INI/**/*.ini\" ] } ] }");

        Assert.False(ModBuilderViewModel.ShouldUpdateSampleConfigFile("GeneralsGamePatch2", itemsFile));
    }

    [Fact]
    public async Task ShouldUpdateSampleConfigFile_WithLegacyLemonItemsFile_ReturnsTrueAsync()
    {
        var configDir = Path.Combine(_tempDir, "LegacyLemon", "config");
        Directory.CreateDirectory(configDir);
        var itemsFile = Path.Combine(configDir, "ModBundleItems.json");
        await File.WriteAllTextAsync(itemsFile, "{ \"BundleItems\": [ { \"Name\": \"LemonControlBarWindows_720p\", \"TargetDir\": \"Window\", \"SourceFiles\": [ \"GameFilesEdited/Window/720p/**/*.wnd\" ] } ] }");

        Assert.True(ModBuilderViewModel.ShouldUpdateSampleConfigFile("LemonControlBar", itemsFile));
    }

    [Fact]
    public async Task ShouldUpdateSampleConfigFile_WithCurrentLemonItemsFile_ReturnsFalseAsync()
    {
        var configDir = Path.Combine(_tempDir, "CurrentLemon", "config");
        Directory.CreateDirectory(configDir);
        var itemsFile = Path.Combine(configDir, "ModBundleItems.json");
        await File.WriteAllTextAsync(itemsFile, "{ \"BundleItems\": [ { \"Name\": \"LemonControlBarArt1080\", \"SourceFiles\": [ \"GameFilesEdited/Gen1080/Art/**/*.dds\" ] } ] }");

        Assert.False(ModBuilderViewModel.ShouldUpdateSampleConfigFile("LemonControlBar", itemsFile));
    }

    [Fact]
    public void ShouldUpdateSampleConfigFile_WithMissingFile_ReturnsTrue()
    {
        var missing = Path.Combine(_tempDir, "Missing", "config", "ModBundleItems.json");

        Assert.True(ModBuilderViewModel.ShouldUpdateSampleConfigFile("LemonControlBar", missing));
    }
}
