// <copyright file="ModBuilderViewModelTests.cs" company="Enowx Labs">
// Copyright (c) Enowx Labs. All rights reserved.
// </copyright>

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.ViewModels;

using System;
using System.IO;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Tools.ModBuilder;
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
    public void InitialState_IsUnloadedAndReady()
    {
        var viewModel = CreateViewModel();

        Assert.Null(viewModel.CurrentProject);
        Assert.False(viewModel.IsProjectLoaded);
        Assert.Equal("Ready", viewModel.StatusMessage);
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
    public void ClearOutput_ClearsBuildLogAndUpdatesStatus()
    {
        var viewModel = CreateViewModel();
        viewModel.BuildLog.Add("Sample build log entry");

        viewModel.ClearOutputCommand.Execute(null);

        Assert.Equal("Build output cleared", viewModel.StatusMessage);
    }

    private ModBuilderViewModel CreateViewModel()
    {
        return new ModBuilderViewModel(
            _mockBuildEngine.Object,
            _mockProjectConfigService.Object,
            _mockConfigLoader.Object,
            _mockProjectStructureGenerator.Object,
            _mockNotificationService.Object,
            _fileManager,
            _mockLoggerFactory.Object,
            _mockLogger.Object);
    }
}
