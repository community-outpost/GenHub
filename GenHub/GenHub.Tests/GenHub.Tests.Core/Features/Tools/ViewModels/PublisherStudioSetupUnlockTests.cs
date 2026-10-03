using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Regression tests verifying that saving a publisher profile unlocks the studio tabs.
/// </summary>
public sealed class PublisherStudioSetupUnlockTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly Mock<IPublisherStudioService> _studioServiceMock = new();
    private readonly Mock<IPublisherStudioDialogService> _dialogServiceMock = new();
    private readonly PublisherStudioViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherStudioSetupUnlockTests"/> class.
    /// </summary>
    public PublisherStudioSetupUnlockTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "GenHubPublisherUnlockTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);

        var configProviderMock = new Mock<IConfigurationProviderService>();
        configProviderMock.Setup(x => x.GetApplicationDataPath()).Returns(_testDirectory);

        _studioServiceMock
            .Setup(x => x.CreateProjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, CancellationToken _) => OperationResult<PublisherStudioProject>.CreateSuccess(new PublisherStudioProject
            {
                ProjectName = name,
                ProjectPath = string.Empty,
                Catalog = new PublisherCatalog
                {
                    Publisher = new PublisherProfile { Id = string.Empty, Name = string.Empty },
                },
            }));
        _studioServiceMock
            .Setup(x => x.SaveProjectAsync(It.IsAny<PublisherStudioProject>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        _viewModel = new PublisherStudioViewModel(
            new Mock<ILogger<PublisherStudioViewModel>>().Object,
            _studioServiceMock.Object,
            _dialogServiceMock.Object,
            configurationProvider: configProviderMock.Object);
    }

    /// <summary>
    /// Disposes of the test resources.
    /// </summary>
    public void Dispose()
    {
        _viewModel.Dispose();
        try
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Verifies that saving a valid publisher profile flips setup to complete and notifies the UI so tabs unlock.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SaveProfile_WithValidPublisher_NotifiesSetupCompleteSoTabsUnlockAsync()
    {
        await _viewModel.CreateNewProjectCommand.ExecuteAsync(null);
        Assert.NotNull(_viewModel.PublisherProfileViewModel);
        Assert.False(_viewModel.IsSetupComplete);

        var notifiedProperties = new List<string?>();
        _viewModel.PropertyChanged += (_, args) => notifiedProperties.Add(args.PropertyName);

        _viewModel.PublisherProfileViewModel.PublisherId = "testtest";
        _viewModel.PublisherProfileViewModel.PublisherName = "test";
        notifiedProperties.Clear();

        await _viewModel.PublisherProfileViewModel.SaveProfileCommand.ExecuteAsync(null);

        Assert.True(_viewModel.IsSetupComplete);
        Assert.Contains(nameof(PublisherStudioViewModel.IsSetupComplete), notifiedProperties);
        Assert.Contains(nameof(PublisherStudioViewModel.ShouldShowSetupOverlay), notifiedProperties);
    }

    /// <summary>
    /// Verifies that an incomplete project forces the studio to the Hosting tab and disables the Referrals tab.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task IncompleteSetup_ForcesHostingTab_AndDisablesReferralsAsync()
    {
        await _viewModel.CreateNewProjectCommand.ExecuteAsync(null);
        Assert.False(_viewModel.IsSetupComplete);

        // Should default to Hosting tab for cloud-first flow
        Assert.Equal(PublisherStudioViewModel.TabHostingStorage, _viewModel.SelectedTabIndex);

        // Referrals tab must be hidden/disabled
        Assert.False(_viewModel.IsReferralsTabVisible);

        // Attempting to navigate to Referrals must be blocked
        _viewModel.SelectTabCommand.Execute(PublisherStudioViewModel.TabReferrals);
        Assert.Equal(PublisherStudioViewModel.TabHostingStorage, _viewModel.SelectedTabIndex);

        // Attempting to navigate to Content Library or Publish when setup is incomplete redirects to Hosting
        _viewModel.SelectTabCommand.Execute(PublisherStudioViewModel.TabCatalogs);
        Assert.Equal(PublisherStudioViewModel.TabHostingStorage, _viewModel.SelectedTabIndex);

        _viewModel.SelectTabCommand.Execute(PublisherStudioViewModel.TabPublishShare);
        Assert.Equal(PublisherStudioViewModel.TabHostingStorage, _viewModel.SelectedTabIndex);

        // Hosting and Profile tabs are not blocked by the setup overlay
        _viewModel.SelectTabCommand.Execute(PublisherStudioViewModel.TabHostingStorage);
        Assert.False(_viewModel.ShouldShowSetupOverlay);

        _viewModel.SelectTabCommand.Execute(PublisherStudioViewModel.TabProfile);
        Assert.False(_viewModel.ShouldShowSetupOverlay);
    }

    /// <summary>
    /// Verifies that connecting a cloud hosting provider unlocks the studio tabs.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CloudProviderConnected_NotifiesSetupCompleteSoTabsUnlockAsync()
    {
        await _viewModel.CreateNewProjectCommand.ExecuteAsync(null);
        Assert.NotNull(_viewModel.PublishShareViewModel);
        Assert.False(_viewModel.IsSetupComplete);

        var notifiedProperties = new List<string?>();
        _viewModel.PropertyChanged += (_, args) => notifiedProperties.Add(args.PropertyName);

        var mockProvider = new Mock<IHostingProvider>();
        mockProvider.Setup(p => p.ProviderId).Returns("test_cloud");
        mockProvider.Setup(p => p.DisplayName).Returns("Test Cloud");
        mockProvider.Setup(p => p.IsAuthenticated).Returns(true);

        _viewModel.PublishShareViewModel.HostingProviders.Add(mockProvider.Object);
        _viewModel.PublishShareViewModel.SelectedHostingProvider = mockProvider.Object;

        _viewModel.PublishShareViewModel.AuthenticationChangedCallback?.Invoke();

        Assert.True(_viewModel.IsSetupComplete);
        Assert.Contains(nameof(PublisherStudioViewModel.IsSetupComplete), notifiedProperties);
        Assert.Contains(nameof(PublisherStudioViewModel.ShouldShowSetupOverlay), notifiedProperties);
    }
}
