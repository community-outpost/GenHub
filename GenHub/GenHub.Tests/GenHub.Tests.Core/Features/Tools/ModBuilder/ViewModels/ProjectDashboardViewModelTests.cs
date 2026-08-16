// <copyright file="ProjectDashboardViewModelTests.cs" company="Enowx Labs">
// Copyright (c) Enowx Labs. All rights reserved.
// </copyright>

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.ViewModels;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Results.ModBuilder;
using GenHub.Features.Tools.ModBuilder.Models;
using GenHub.Features.Tools.ModBuilder.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for <see cref="ProjectDashboardViewModel"/>.
/// </summary>
public class ProjectDashboardViewModelTests : IDisposable
{
    private readonly Mock<IProjectConfigService> _mockProjectConfigService;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly Mock<ILogger<ProjectDashboardViewModel>> _mockLogger;
    private readonly string _tempDir;

    public ProjectDashboardViewModelTests()
    {
        _mockProjectConfigService = new Mock<IProjectConfigService>();
        _mockNotificationService = new Mock<INotificationService>();
        _mockLogger = new Mock<ILogger<ProjectDashboardViewModel>>();
        _tempDir = Path.Combine(Path.GetTempPath(), "GenHub_DashboardTests_" + Guid.NewGuid().ToString("N"));
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
    public async Task InitializeAsync_WhenRecentProjectsExist_PopulatesRecentProjectsCollection()
    {
        var projectFile = Path.Combine(_tempDir, "SampleMod.mbproj");
        await File.WriteAllTextAsync(projectFile, "{}");

        _mockProjectConfigService
            .Setup(s => s.GetRecentProjectsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProjectOperationResult<List<string>>.CreateSuccess([projectFile], TimeSpan.Zero));

        var viewModel = new ProjectDashboardViewModel(
            _mockProjectConfigService.Object,
            _mockNotificationService.Object,
            _mockLogger.Object);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.HasRecentProjects);
        Assert.Single(viewModel.RecentProjects);
        Assert.Equal("SampleMod", viewModel.RecentProjects[0].Name);
        Assert.Equal(projectFile, viewModel.RecentProjects[0].Path);
        Assert.Equal(1, viewModel.TotalProjects);
    }

    [Fact]
    public async Task InitializeAsync_WhenNoRecentProjects_SetsHasRecentProjectsToFalse()
    {
        _mockProjectConfigService
            .Setup(s => s.GetRecentProjectsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProjectOperationResult<List<string>>.CreateSuccess([], TimeSpan.Zero));

        var viewModel = new ProjectDashboardViewModel(
            _mockProjectConfigService.Object,
            _mockNotificationService.Object,
            _mockLogger.Object);

        await viewModel.InitializeAsync();

        Assert.False(viewModel.HasRecentProjects);
        Assert.Empty(viewModel.RecentProjects);
        Assert.Equal(0, viewModel.TotalProjects);
    }

    [Fact]
    public void OpenRecentProject_RaisesProjectSelectedEvent()
    {
        var viewModel = new ProjectDashboardViewModel(
            _mockProjectConfigService.Object,
            _mockNotificationService.Object,
            _mockLogger.Object);

        string? selectedPath = null;
        viewModel.ProjectSelected += (s, path) => selectedPath = path;

        var testPath = Path.Combine(_tempDir, "Mod.mbproj");
        var projectInfo = new RecentProjectInfo
        {
            Name = "Mod",
            Path = testPath,
        };

        viewModel.OpenRecentProjectCommand.Execute(projectInfo);

        Assert.Equal(testPath, selectedPath);
    }
}
