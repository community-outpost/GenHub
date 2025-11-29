using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Features.GitHub.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.ViewModels;

/// <summary>
/// Tests for InstallationViewModel.
/// </summary>
public class InstallationViewModelTests
{
    private readonly Mock<IContentStorageService> _contentStorageServiceMock;
    private readonly Mock<IContentOrchestrator> _contentOrchestratorMock;
    private readonly Mock<IDependencyResolver> _dependencyResolverMock;
    private readonly Mock<ILogger<InstallationViewModel>> _loggerMock;
    private readonly InstallationViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="InstallationViewModelTests"/> class.
    /// </summary>
    public InstallationViewModelTests()
    {
        _contentStorageServiceMock = new Mock<IContentStorageService>();
        _contentOrchestratorMock = new Mock<IContentOrchestrator>();
        _dependencyResolverMock = new Mock<IDependencyResolver>();
        _loggerMock = new Mock<ILogger<InstallationViewModel>>();
        _viewModel = new InstallationViewModel(
            _contentStorageServiceMock.Object,
            _contentOrchestratorMock.Object,
            _dependencyResolverMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that constructor initializes properties correctly.
    /// </summary>
    [Fact]
    public void Constructor_InitializesPropertiesCorrectly()
    {
        // Assert
        Assert.False(_viewModel.IsInstalling);
        Assert.Equal(0, _viewModel.InstallationProgress);
        Assert.Equal("Ready to install", _viewModel.StatusMessage);
        Assert.Null(_viewModel.CurrentItem);
        Assert.NotNull(_viewModel.InstallationLog);
        Assert.Empty(_viewModel.InstallationLog);
    }

    /// <summary>
    /// Verifies that CancelInstallation resets installation state.
    /// </summary>
    [Fact]
    public void CancelInstallation_ResetsInstallationState()
    {
        // Arrange
        _viewModel.IsInstalling = true;
        _viewModel.InstallationProgress = 50;
        _viewModel.StatusMessage = "Installing...";
        _viewModel.InstallationLog.Add("Test log entry");

        // Act
        _viewModel.CancelInstallation();

        // Assert
        Assert.False(_viewModel.IsInstalling);
        Assert.Equal(0, _viewModel.InstallationProgress);
        Assert.Equal("Installation cancelled", _viewModel.StatusMessage);
        Assert.Contains("Installation cancelled", _viewModel.InstallationLog);
    }

    /// <summary>
    /// Verifies that IsInstalling setter raises PropertyChanged event.
    /// </summary>
    [Fact]
    public void IsInstalling_PropertyChanged_IsRaised()
    {
        // Arrange
        var propertyChangedRaised = false;
        _viewModel.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(_viewModel.IsInstalling))
            {
                propertyChangedRaised = true;
            }
        };

        // Act
        _viewModel.IsInstalling = true;

        // Assert
        Assert.True(propertyChangedRaised);
        Assert.True(_viewModel.IsInstalling);
    }

    /// <summary>
    /// Verifies that CancelInstallation stops installation and logs the event.
    /// </summary>
    [Fact]
    public void CancelInstallation_StopsInstallationAndLogs()
    {
        _viewModel.IsInstalling = true;
        _viewModel.InstallationProgress = 50;

        // Act
        _viewModel.CancelInstallation();

        // Assert
        Assert.Equal("Installation cancelled", _viewModel.StatusMessage);
        Assert.False(_viewModel.IsInstalling);
        Assert.Equal(0, _viewModel.InstallationProgress);
        Assert.Contains("Installation cancelled", _viewModel.InstallationLog);
    }
}
