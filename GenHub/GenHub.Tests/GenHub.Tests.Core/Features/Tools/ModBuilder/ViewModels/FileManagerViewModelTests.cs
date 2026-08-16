// <copyright file="FileManagerViewModelTests.cs" company="Enowx Labs">
// Copyright (c) Enowx Labs. All rights reserved.
// </copyright>

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.ViewModels;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using GenHub.Features.Tools.ModBuilder.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for <see cref="FileManagerViewModel"/>.
/// </summary>
public class FileManagerViewModelTests : IDisposable
{
    private readonly Mock<IGameInstallationService> _mockGameInstallService;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly Mock<ILogger<FileManagerViewModel>> _mockLogger;
    private readonly string _tempDir;
    private readonly string _projectDir;
    private readonly string _gameDir;

    public FileManagerViewModelTests()
    {
        _mockGameInstallService = new Mock<IGameInstallationService>();
        _mockNotificationService = new Mock<INotificationService>();
        _mockLogger = new Mock<ILogger<FileManagerViewModel>>();

        _tempDir = Path.Combine(Path.GetTempPath(), "GenHub_FileManagerTests_" + Guid.NewGuid().ToString("N"));
        _projectDir = Path.Combine(_tempDir, "Project");
        _gameDir = Path.Combine(_tempDir, "GameInstall");

        Directory.CreateDirectory(_projectDir);
        Directory.CreateDirectory(_gameDir);
        Directory.CreateDirectory(Path.Combine(_projectDir, "GameFilesEdited"));
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
    public async Task InitializeAsync_LoadsInstallationsAndPopulatesFileTrees()
    {
        // Create sample files
        var gameIni = Path.Combine(_gameDir, "GameData.ini");
        await File.WriteAllTextAsync(gameIni, "Stock INI Content");
        await File.WriteAllTextAsync(Path.Combine(_gameDir, "generals.exe"), "mock exe");

        var modIni = Path.Combine(_projectDir, "GameFilesEdited", "GameData.ini");
        await File.WriteAllTextAsync(modIni, "Modified INI Content");

        var mockInstall = new GameInstallation(
            _gameDir,
            GameInstallationType.Steam);
        mockInstall.SetPaths(_gameDir, _gameDir);

        _mockGameInstallService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([mockInstall]));

        var viewModel = new FileManagerViewModel(
            _mockGameInstallService.Object,
            _mockNotificationService.Object,
            _mockLogger.Object);

        await viewModel.InitializeAsync(_projectDir);

        Assert.NotEmpty(viewModel.AvailableInstallations);
        Assert.NotNull(viewModel.SelectedInstallation);
        Assert.NotEmpty(viewModel.FileTypeFilters);
    }
}
