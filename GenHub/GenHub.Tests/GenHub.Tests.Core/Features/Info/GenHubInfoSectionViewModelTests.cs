using System.ComponentModel;
using FluentAssertions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Info;
using GenHub.Features.Info.Services;
using GenHub.Features.Info.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Info;

/// <summary>
/// Unit tests for <see cref="GenHubInfoSectionViewModel"/>.
/// </summary>
public class GenHubInfoSectionViewModelTests
{
    private readonly Mock<IInfoContentProvider> _contentProviderMock = new();
    private readonly Mock<IGitHubApiClient> _gitHubMock = new();
    private readonly Mock<ILogger<ChangelogsViewModel>> _changelogLoggerMock = new();
    private readonly Mock<IGeneralsOnlinePatchNotesService> _patchNotesMock = new();
    private readonly Mock<ILogger<GeneralsOnlineChangelogViewModel>> _goLoggerMock = new();
    private readonly Mock<ILocalizationService> _localizationServiceMock = new();

    private GenHubInfoSectionViewModel CreateViewModel()
    {
        var changelogVm = new ChangelogsViewModel(_gitHubMock.Object, _changelogLoggerMock.Object);
        var goChangelogVm = new GeneralsOnlineChangelogViewModel(_patchNotesMock.Object, _goLoggerMock.Object);

        return new GenHubInfoSectionViewModel(
            _contentProviderMock.Object,
            changelogVm,
            goChangelogVm,
            localizationService: _localizationServiceMock.Object);
    }

    /// <summary>
    /// Tests that Dispose unsubscribes from ILocalizationService PropertyChanged.
    /// </summary>
    [Fact]
    public async Task Dispose_UnsubscribesFromLocalizationServiceAsync()
    {
        var vm = CreateViewModel();
        await vm.InitializeAsync();

        var titleChanged = false;
        vm.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(GenHubInfoSectionViewModel.Title))
            {
                titleChanged = true;
            }
        };

        // Before dispose, event triggers PropertyChanged
        _localizationServiceMock.Raise(
            l => l.PropertyChanged += null,
            new PropertyChangedEventArgs(nameof(ILocalizationService.CurrentCulture)));

        titleChanged.Should().BeTrue();

        // Reset and dispose
        titleChanged = false;
        vm.Dispose();

        _localizationServiceMock.Raise(
            l => l.PropertyChanged += null,
            new PropertyChangedEventArgs(nameof(ILocalizationService.CurrentCulture)));

        titleChanged.Should().BeFalse();
    }
}
