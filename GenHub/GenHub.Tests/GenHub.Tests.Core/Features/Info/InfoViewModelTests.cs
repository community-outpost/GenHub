using System.ComponentModel;
using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Info;
using GenHub.Features.Info.ViewModels;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Info;

/// <summary>
/// Unit tests for <see cref="InfoViewModel"/>.
/// </summary>
public class InfoViewModelTests
{
    private readonly Mock<ILocalizationService> _localizationServiceMock = new();

    /// <summary>
    /// Tests that the constructor initializes Modules collection correctly.
    /// </summary>
    [Fact]
    public void Constructor_InitializesModulesCollection()
    {
        var vm = new InfoViewModel([], _localizationServiceMock.Object);

        vm.Modules.Should().NotBeNull();
        vm.Modules.Should().ContainInOrder(
            InfoConstants.ModuleGuide,
            InfoConstants.ModuleZeroHour,
            InfoConstants.ModuleGeneralsOnline);
    }

    /// <summary>
    /// Tests that a culture change event on ILocalizationService refreshes the Modules collection.
    /// </summary>
    [Fact]
    public void CultureChanged_RefreshesModulesCollectionAndRaisesPropertyChanged()
    {
        var vm = new InfoViewModel([], _localizationServiceMock.Object);
        var initialModules = vm.Modules;
        var propertyChangedRaised = false;
        vm.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(InfoViewModel.Modules))
            {
                propertyChangedRaised = true;
            }
        };

        _localizationServiceMock.Raise(
            l => l.PropertyChanged += null,
            new PropertyChangedEventArgs(nameof(ILocalizationService.CurrentCulture)));

        propertyChangedRaised.Should().BeTrue();
        vm.Modules.Should().NotBeSameAs(initialModules);
        vm.Modules.Should().ContainInOrder(
            InfoConstants.ModuleGuide,
            InfoConstants.ModuleZeroHour,
            InfoConstants.ModuleGeneralsOnline);
    }

    /// <summary>
    /// Tests that Dispose unsubscribes from ILocalizationService PropertyChanged.
    /// </summary>
    [Fact]
    public void Dispose_UnsubscribesFromLocalizationService()
    {
        var vm = new InfoViewModel([], _localizationServiceMock.Object);
        vm.Dispose();

        var propertyChangedRaised = false;
        vm.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(InfoViewModel.Modules))
            {
                propertyChangedRaised = true;
            }
        };

        _localizationServiceMock.Raise(
            l => l.PropertyChanged += null,
            new PropertyChangedEventArgs(nameof(ILocalizationService.CurrentCulture)));

        propertyChangedRaised.Should().BeFalse();
    }
}
