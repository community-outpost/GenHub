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
}
