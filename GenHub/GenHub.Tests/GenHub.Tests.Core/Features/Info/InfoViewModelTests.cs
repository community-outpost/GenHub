using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Info;
using GenHub.Core.Models.Info;
using GenHub.Features.Info.Services;
using GenHub.Features.Info.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
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
    /// Tests that programmatic section navigation syncs back to the sidebar selection.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GenHubSectionSelectionChange_UpdatesSelectedSidebarItemAsync()
    {
        var contentProviderMock = new Mock<IInfoContentProvider>();
        contentProviderMock
            .Setup(p => p.GetAllSectionsAsync())
            .ReturnsAsync(new List<InfoSection>
            {
                new() { Id = "sec1", Title = "Section 1", Cards = new List<InfoCard>() },
                new() { Id = "sec2", Title = "Section 2", Cards = new List<InfoCard>() },
            });
        var section = new GenHubInfoSectionViewModel(
            contentProviderMock.Object,
            new ChangelogsViewModel(Mock.Of<IGitHubApiClient>(), Mock.Of<ILogger<ChangelogsViewModel>>()),
            new GeneralsOnlineChangelogViewModel(Mock.Of<IGeneralsOnlinePatchNotesService>(), Mock.Of<ILogger<GeneralsOnlineChangelogViewModel>>()));
        await section.InitializeAsync();

        var vm = new InfoViewModel([section], _localizationServiceMock.Object);
        section.SelectedSection = section.Sections[1];

        vm.SelectedSidebarItem.Should().Be(section.Sections[1]);
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
