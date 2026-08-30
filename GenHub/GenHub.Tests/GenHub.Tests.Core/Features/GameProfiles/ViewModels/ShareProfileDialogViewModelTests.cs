using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameProfiles;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels;

/// <summary>
/// Unit tests for <see cref="ShareProfileDialogViewModel"/>.
/// </summary>
public class ShareProfileDialogViewModelTests
{
    private readonly Mock<IProfileSharingService> _sharingServiceMock = new();

    /// <summary>
    /// Verifies initialization and properties of the ShareProfileDialogViewModel.
    /// </summary>
    [Fact]
    public void Constructor_Should_InitializePropertiesCorrectly()
    {
        // Arrange
        var profile = new GameProfile
        {
            Id = "prof-1",
            Name = "ZH Ranked",
            Description = "Ranked matchmaking profile",
            ThemeColor = "#4CAF50",
            GameClient = new GameClient { Version = "1.04" },
        };
        var shareUri = "genhub://profile/import?data=TEST_DATA";

        // Act
        var vm = new ShareProfileDialogViewModel(
            "prof-1",
            profile,
            shareUri,
            _sharingServiceMock.Object,
            NullLogger<ShareProfileDialogViewModel>.Instance);

        // Assert
        Assert.Equal("ZH Ranked", vm.ProfileName);
        Assert.Equal("genhub://profile/import?data=TEST_DATA", vm.ShareUri);
        Assert.Equal("#4CAF50", vm.ThemeColor);
    }

    /// <summary>
    /// Verifies that close request triggers CloseRequested event.
    /// </summary>
    [Fact]
    public void CloseCommand_Should_RaiseCloseRequestedEvent()
    {
        // Arrange
        var profile = new GameProfile { Id = "prof-1", Name = "ZH Ranked" };
        var vm = new ShareProfileDialogViewModel(
            "prof-1",
            profile,
            "genhub://profile/import?data=123",
            _sharingServiceMock.Object,
            NullLogger<ShareProfileDialogViewModel>.Instance);

        bool closed = false;
        vm.CloseRequested += (s, e) => closed = true;

        // Act
        vm.CloseCommand.Execute(null);

        // Assert
        Assert.True(closed);
    }

    /// <summary>
    /// Verifies that constructor sets HasCloudUploads when local content is present in enabled content IDs.
    /// </summary>
    [Fact]
    public void Constructor_Should_DetectCloudUploads_WhenLocalContentIsPresent()
    {
        // Arrange
        var profile = new GameProfile
        {
            Id = "prof-local",
            Name = "Local Setup",
            EnabledContentIds = ["1.0.local.mod.custommod"],
        };

        // Act
        var vm = new ShareProfileDialogViewModel(
            "prof-local",
            profile,
            "genhub://profile/import?data=local",
            _sharingServiceMock.Object,
            NullLogger<ShareProfileDialogViewModel>.Instance);

        // Assert
        Assert.True(vm.HasCloudUploads);
        Assert.NotEmpty(vm.CloudUploadDetails);
    }

    /// <summary>
    /// Verifies that constructor loads quota usage and sets warning when quota is exceeded.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Constructor_Should_LoadQuotaAndSetWarning_WhenQuotaExceededAsync()
    {
        // Arrange
        var profile = new GameProfile
        {
            Id = "prof-local",
            Name = "Local Setup",
            EnabledContentIds = ["1.0.local.map.custommap"],
        };

        var uploadHistoryMock = new Mock<GenHub.Core.Interfaces.Common.IUploadHistoryService>();
        uploadHistoryMock.Setup(u => u.GetUsageInfoAsync(It.IsAny<string?>()))
            .ReturnsAsync(new GenHub.Core.Models.Common.UsageInfo(10 * 1024 * 1024, 10 * 1024 * 1024, DateTime.UtcNow.AddDays(7)));

        // Act
        var vm = new ShareProfileDialogViewModel(
            "prof-local",
            profile,
            "genhub://profile/import?data=local",
            _sharingServiceMock.Object,
            NullLogger<ShareProfileDialogViewModel>.Instance,
            uploadHistoryMock.Object);

        // Allow async load to finish
        await Task.Delay(100);

        // Assert
        Assert.True(vm.HasCloudUploads);
        Assert.True(vm.IsQuotaExceeded);
        Assert.True(vm.HasUploadWarnings);
        Assert.Contains("Cloud storage limit reached", vm.UploadWarningMessage);
    }

    /// <summary>
    /// Verifies that constructor does not generate a link or initiate upload when local content is present.
    /// </summary>
    [Fact]
    public void Constructor_Should_NotGenerateOrUploadLink_WhenLocalContentIsPresent()
    {
        // Arrange
        var profile = new GameProfile
        {
            Id = "prof-local",
            Name = "Local Setup",
            EnabledContentIds = ["1.0.local.map.custommap"],
        };

        // Act
        var vm = new ShareProfileDialogViewModel(
            "prof-local",
            profile,
            _sharingServiceMock.Object,
            NullLogger<ShareProfileDialogViewModel>.Instance);

        // Assert
        Assert.True(vm.HasCloudUploads);
        Assert.False(vm.IsShareUriGenerated);
        Assert.Empty(vm.ShareUri);
        _sharingServiceMock.Verify(s => s.ExportProfileToUriAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that executing GenerateShareLinkCommand generates the share link upon user confirmation.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GenerateShareLinkCommand_Should_CallExportProfileToUri_AndSetShareUriAsync()
    {
        // Arrange
        var profile = new GameProfile
        {
            Id = "prof-local",
            Name = "Local Setup",
            EnabledContentIds = ["1.0.local.map.custommap"],
        };

        _sharingServiceMock.Setup(s => s.ExportProfileToUriAsync("prof-local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateSuccess("genhub://profile/import?data=CONFIRMED_DATA"));

        var vm = new ShareProfileDialogViewModel(
            "prof-local",
            profile,
            _sharingServiceMock.Object,
            NullLogger<ShareProfileDialogViewModel>.Instance);

        // Act
        await vm.GenerateShareLinkCommand.ExecuteAsync(null);

        // Assert
        Assert.True(vm.IsShareUriGenerated);
        Assert.Equal("genhub://profile/import?data=CONFIRMED_DATA", vm.ShareUri);
        _sharingServiceMock.Verify(s => s.ExportProfileToUriAsync("prof-local", It.IsAny<CancellationToken>()), Times.Once);
    }
}
