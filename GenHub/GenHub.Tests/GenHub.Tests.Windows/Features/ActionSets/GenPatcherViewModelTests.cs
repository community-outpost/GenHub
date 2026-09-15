using System.ComponentModel;
using FluentAssertions;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Windows.Features.ActionSets.Infrastructure;
using GenHub.Windows.Features.ActionSets.UI;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Windows.Features.ActionSets;

/// <summary>
/// Unit tests for <see cref="GenPatcherViewModel"/>.
/// </summary>
public class GenPatcherViewModelTests
{
    private readonly Mock<IActionSetOrchestrator> _orchestratorMock = new();
    private readonly Mock<IGameInstallationDetector> _installationDetectorMock = new();
    private readonly Mock<IRegistryService> _registryServiceMock = new();
    private readonly Mock<INotificationService> _notificationServiceMock = new();
    private readonly Mock<IDialogService> _dialogServiceMock = new();
    private readonly Mock<ILogger<GenPatcherViewModel>> _loggerMock = new();
    private readonly Mock<ILocalizationService> _localizationServiceMock = new();

    private GenPatcherViewModel CreateViewModel()
    {
        return new GenPatcherViewModel(
            _orchestratorMock.Object,
            _installationDetectorMock.Object,
            _registryServiceMock.Object,
            _notificationServiceMock.Object,
            _dialogServiceMock.Object,
            _loggerMock.Object,
            _localizationServiceMock.Object);
    }

    /// <summary>
    /// Tests that Dispose unsubscribes from ILocalizationService PropertyChanged.
    /// </summary>
    [Fact]
    public async Task Dispose_UnsubscribesFromLocalizationServiceAsync()
    {
        var vm = CreateViewModel();
        await vm.InitializeAsync();

        var propertyChangedRaised = false;
        vm.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(GenPatcherViewModel.ProgressSummaryText))
            {
                propertyChangedRaised = true;
            }
        };

        // Before dispose, event triggers OnLocalizationChanged which updates metrics and triggers PropertyChanged
        _localizationServiceMock.Raise(
            l => l.PropertyChanged += null,
            new PropertyChangedEventArgs(nameof(ILocalizationService.CurrentCulture)));

        propertyChangedRaised.Should().BeTrue();

        // Reset and dispose
        propertyChangedRaised = false;
        vm.Dispose();

        _localizationServiceMock.Raise(
            l => l.PropertyChanged += null,
            new PropertyChangedEventArgs(nameof(ILocalizationService.CurrentCulture)));

        propertyChangedRaised.Should().BeFalse();
    }
}
