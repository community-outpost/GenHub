// <copyright file="FileManagerViewModelTests.cs" company="Enowx Labs">
// Copyright (c) Enowx Labs. All rights reserved.
// </copyright>

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.ViewModels;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.WndEditor;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using GenHub.Features.Tools.ModBuilder.Models;
using GenHub.Features.Tools.ModBuilder.ViewModels;
using GenHub.Features.Tools.WndEditor.Services;
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
    private readonly Mock<IWndDocumentService> _mockWndDocumentService;
    private readonly Mock<ILocalizationService> _mockLocalizationService;
    private readonly Mock<ILogger<FileManagerViewModel>> _mockLogger;
    private readonly string _tempDir;
    private readonly string _projectDir;
    private readonly string _gameDir;

    public FileManagerViewModelTests()
    {
        _mockGameInstallService = new Mock<IGameInstallationService>();
        _mockNotificationService = new Mock<INotificationService>();
        _mockWndDocumentService = new Mock<IWndDocumentService>();
        _mockLocalizationService = new Mock<ILocalizationService>();
        _mockLogger = new Mock<ILogger<FileManagerViewModel>>();

        _mockLocalizationService
            .Setup(s => s.GetString(It.IsAny<string>(), It.IsAny<object?[]>()))
            .Returns((string key, object?[] args) => key);

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

        // Installation presence is determined by retail archives, not executables.
        await File.WriteAllTextAsync(Path.Combine(_gameDir, GameClientConstants.GeneralsIniBig), "mock archive");

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
            _mockWndDocumentService.Object,
            _mockLocalizationService.Object,
            _mockLogger.Object);

        await viewModel.InitializeAsync(_projectDir);

        Assert.NotEmpty(viewModel.AvailableInstallations);
        Assert.NotNull(viewModel.SelectedInstallation);
        Assert.NotEmpty(viewModel.FileTypeFilters);
    }

    [Fact]
    public async Task InitializeAsync_WhenCancelled_SetsCancelledStatusAndDoesNotFail()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var viewModel = new FileManagerViewModel(
            _mockGameInstallService.Object,
            _mockNotificationService.Object,
            _mockWndDocumentService.Object,
            _mockLocalizationService.Object,
            _mockLogger.Object);

        await viewModel.InitializeAsync(_projectDir, cancellationToken: cts.Token);

        Assert.Equal("File loading canceled", viewModel.StatusMessage);
        Assert.False(viewModel.IsLoading);
    }

    [Fact]
    public async Task ValidateWndFilesCommand_WithValidWndFile_ShowsSuccess()
    {
        var wndPath = Path.Combine(_projectDir, "GameFilesEdited", "Menu.wnd");
        await File.WriteAllTextAsync(wndPath, "FILE_VERSION = 2;\nWINDOW\n  WINDOWTYPE = USER;\nEND\n");

        var viewModel = CreateViewModelWithRealWndService();
        viewModel.SelectedProjectFile = new FileTreeNode { Name = "Menu.wnd", FullPath = wndPath, IsDirectory = false };

        await viewModel.ValidateWndFilesCommand.ExecuteAsync(null);

        _mockNotificationService.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
        Assert.False(viewModel.IsLoading);
    }

    [Fact]
    public async Task ValidateWndFilesCommand_WithInvalidWndFile_ShowsWarning()
    {
        var wndPath = Path.Combine(_projectDir, "GameFilesEdited", "Broken.wnd");
        await File.WriteAllTextAsync(wndPath, "WINDOW\n  WINDOWTYPE = USER\nEND\n");

        var viewModel = CreateViewModelWithRealWndService();
        viewModel.SelectedProjectFile = new FileTreeNode { Name = "Broken.wnd", FullPath = wndPath, IsDirectory = false };

        await viewModel.ValidateWndFilesCommand.ExecuteAsync(null);

        _mockNotificationService.Verify(
            n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    [Fact]
    public async Task ValidateWndFilesCommand_WithNoSelection_ShowsInfo()
    {
        var viewModel = CreateViewModelWithRealWndService();

        await viewModel.ValidateWndFilesCommand.ExecuteAsync(null);

        _mockNotificationService.Verify(
            n => n.ShowInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    [Fact]
    public async Task FormatWndFilesCommand_WithMessyWndFile_FormatsAndShowsSuccess()
    {
        var wndPath = Path.Combine(_projectDir, "GameFilesEdited", "Messy.wnd");
        await File.WriteAllTextAsync(wndPath, "FILE_VERSION=2;\nWINDOW\nWINDOWTYPE=USER;\nEND\n");

        var viewModel = CreateViewModelWithRealWndService();
        viewModel.SelectedProjectFile = new FileTreeNode { Name = "Messy.wnd", FullPath = wndPath, IsDirectory = false };

        await viewModel.FormatWndFilesCommand.ExecuteAsync(null);

        var formatted = await File.ReadAllTextAsync(wndPath);
        Assert.Equal("FILE_VERSION = 2;\nWINDOW\n  WINDOWTYPE = USER;\nEND\n", formatted);
        _mockNotificationService.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    private FileManagerViewModel CreateViewModelWithRealWndService()
    {
        var wndService = new WndDocumentService(Mock.Of<ILogger<WndDocumentService>>());
        return new FileManagerViewModel(
            _mockGameInstallService.Object,
            _mockNotificationService.Object,
            wndService,
            _mockLocalizationService.Object,
            _mockLogger.Object);
    }
}
