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
}
