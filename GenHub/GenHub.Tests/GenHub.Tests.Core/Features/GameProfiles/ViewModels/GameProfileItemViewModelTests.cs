using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Features.GameProfiles.ViewModels;
using Moq;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels;

/// <summary>
/// Tests for <see cref="GameProfileItemViewModel"/>.
/// </summary>
public class GameProfileItemViewModelTests
{
    /// <summary>
    /// Verifies construction of <see cref="GameProfileItemViewModel"/>.
    /// </summary>
    [Fact]
    public void CanConstruct()
    {
        var mockProfile = new Mock<IGameProfile>();
        mockProfile.SetupGet(p => p.Version).Returns("1.0");
        mockProfile.SetupGet(p => p.ExecutablePath).Returns("C:/fake/path.exe");
        var vm = new GameProfileItemViewModel("test-profile-id", mockProfile.Object, "icon.png", "cover.jpg");
        Assert.NotNull(vm);
        Assert.Equal("test-profile-id", vm.ProfileId);
    }

    /// <summary>
    /// Verifies that version display is suppressed for local content even if GameClient has a version.
    /// </summary>
    [Fact]
    public void Construction_WithLocalContent_SuppressVersionDisplay()
    {
        // Arrange
        var gameClient = new GenHub.Core.Models.GameClients.GameClient
        {
            Id = "schema.1.local.map.some-map", // local publisher in ID
            Version = "1.0", // Has a version that should be suppressed
            Name = "Local Map",
        };

        var profile = new GenHub.Core.Models.GameProfile.GameProfile
        {
            Id = "test-profile-local",
            Name = "Test Local Profile",
            GameClient = gameClient,
        };

        // Act
        var vm = new GameProfileItemViewModel("test-profile-local", profile, null!, null!);

        // Assert
        Assert.Equal("Local", vm.Publisher); // Extracted from "local" segment
        Assert.Empty(vm.GameVersion ?? string.Empty); // Suppressed
    }

    /// <summary>
    /// Verifies that the copy profile command calls the copy action when executed.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CopyProfileCommand_CallsCopyActionAsync()
    {
        // Arrange
        var mockProfile = new Mock<IGameProfile>();
        mockProfile.SetupGet(p => p.Version).Returns("1.0");
        mockProfile.SetupGet(p => p.ExecutablePath).Returns("C:/fake/path.exe");

        var vm = new GameProfileItemViewModel("test-profile-id", mockProfile.Object, "icon.png", "cover.jpg");

        GameProfileItemViewModel? passedVm = null;
        vm.CopyProfileAction = viewModel =>
        {
            passedVm = viewModel;
            return Task.CompletedTask;
        };

        // Act
        await vm.CopyProfileCommand.ExecuteAsync(null);

        // Assert
        Assert.NotNull(passedVm);
        Assert.Same(vm, passedVm);
    }

    /// <summary>
    /// Verifies that the copy profile command can be executed when copy action is set.
    /// </summary>
    [Fact]
    public void CopyProfileCommand_CanExecute_WhenActionIsSet()
    {
        // Arrange
        var mockProfile = new Mock<IGameProfile>();
        mockProfile.SetupGet(p => p.Version).Returns("1.0");
        mockProfile.SetupGet(p => p.ExecutablePath).Returns("C:/fake/path.exe");

        var vm = new GameProfileItemViewModel("test-profile-id", mockProfile.Object, "icon.png", "cover.jpg")
        {
            CopyProfileAction = _ => Task.CompletedTask,
        };

        // Act & Assert
        Assert.True(vm.CopyProfileCommand.CanExecute(null));
    }

    /// <summary>
    /// Verifies that the copy profile command can be executed even when copy action is null.
    /// </summary>
    [Fact]
    public void CopyProfileCommand_CanExecute_WhenActionIsNull()
    {
        // Arrange
        var mockProfile = new Mock<IGameProfile>();
        mockProfile.SetupGet(p => p.Version).Returns("1.0");
        mockProfile.SetupGet(p => p.ExecutablePath).Returns("C:/fake/path.exe");

        var vm = new GameProfileItemViewModel("test-profile-id", mockProfile.Object, "icon.png", "cover.jpg");

        // Don't set CopyProfileAction

        // Act & Assert
        Assert.True(vm.CopyProfileCommand.CanExecute(null));
    }

    /// <summary>
    /// Verifies that the copy profile command execution is safe when copy action is null.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CopyProfileCommand_Execute_WhenActionIsNull_DoesNotThrowAsync()
    {
        // Arrange
        var mockProfile = new Mock<IGameProfile>();
        mockProfile.SetupGet(p => p.Version).Returns("1.0");
        mockProfile.SetupGet(p => p.ExecutablePath).Returns("C:/fake/path.exe");

        var vm = new GameProfileItemViewModel("test-profile-id", mockProfile.Object, "icon.png", "cover.jpg");

        // Don't set CopyProfileAction (null)

        // Act & Assert - should not throw
        var exception = await Record.ExceptionAsync(() => vm.CopyProfileCommand.ExecuteAsync(null));
        Assert.Null(exception);
    }

    /// <summary>
    /// Verifies that constructing with a publisher game client properly sets version and publisher badges.
    /// </summary>
    [Fact]
    public void Construction_WithPublisherGameClient_SetsVersionAndPublisherBadges()
    {
        // Arrange
        var gameClient = new GenHub.Core.Models.GameClients.GameClient
        {
            Id = "1.104.generalsonline.gameclient.zerohour",
            Name = "Generals Online",
            Version = "000104",
            PublisherType = "GeneralsOnline",
        };

        var profile = new GenHub.Core.Models.GameProfile.GameProfile
        {
            Id = "test-profile-go",
            Name = "Test GO Profile",
            GameClient = gameClient,
        };

        // Act
        var vm = new GameProfileItemViewModel("test-profile-go", profile, null!, null!);

        // Assert
        Assert.Equal("Generals Online", vm.Publisher);
        Assert.Equal("000104", vm.GameVersion);
        Assert.Contains("Generals Online", vm.Description);
    }

    /// <summary>
    /// Verifies that release-date versions like 20260821 are not divided into v202608.21.
    /// </summary>
    [Fact]
    public void Construction_WithDateBasedGameClient_PreservesFullDateVersion()
    {
        // Arrange
        var gameClient = new GenHub.Core.Models.GameClients.GameClient
        {
            Id = "1.20260821.thesuperhackers.gameclient.zerohour",
            Name = "The Super Hackers",
            Version = "20260821",
            PublisherType = "thesuperhackers",
        };

        var profile = new GenHub.Core.Models.GameProfile.GameProfile
        {
            Id = "test-profile-tsh",
            Name = "Test TSH Profile",
            GameClient = gameClient,
        };

        // Act
        var vm = new GameProfileItemViewModel("test-profile-tsh", profile, null!, null!);

        // Assert
        Assert.Equal("The Super Hackers", vm.Publisher);
        Assert.Equal("20260821", vm.GameVersion);
    }

    /// <summary>
    /// Verifies that enabling a patch manifest overrides the version and publisher badges.
    /// </summary>
    [Fact]
    public void Construction_WithEnabledCommunityPatch_SetsVersionAndPublisherBadges()
    {
        // Arrange
        var profile = new GenHub.Core.Models.GameProfile.GameProfile
        {
            Id = "test-profile-patch",
            Name = "Test Patch Profile",
            EnabledContentIds = ["1.106.communityoutpost.patch.zerohour"],
        };

        // Act
        var vm = new GameProfileItemViewModel("test-profile-patch", profile, null!, null!);

        // Assert
        Assert.Equal("Community Outpost", vm.Publisher);
        Assert.Equal("v1.06", vm.GameVersion);
        Assert.Contains("v1.06", vm.Description);
    }

    /// <summary>
    /// Verifies that enabling a patch manifest overrides the publisher and version when using a standard non-publisher game client.
    /// </summary>
    [Fact]
    public void Construction_WithStandardClientAndEnabledPatch_OverridesPublisherAndVersion()
    {
        // Arrange
        var profile = new GenHub.Core.Models.GameProfile.GameProfile
        {
            Id = "test-steam-patch",
            Name = "Steam Patch Profile",
            GameClient = new GenHub.Core.Models.GameClients.GameClient
            {
                Id = "steam",
                Name = "Steam Client",
                PublisherType = "Steam",
                Version = "1.04",
                GameType = GenHub.Core.Models.Enums.GameType.ZeroHour,
            },
            EnabledContentIds = ["1.106.communityoutpost.patch.zerohour"],
        };

        // Act
        var vm = new GameProfileItemViewModel("test-steam-patch", profile, null!, null!);

        // Assert
        Assert.Equal("Community Outpost", vm.Publisher);
        Assert.Equal("v1.06", vm.GameVersion);
    }

    /// <summary>
    /// Verifies that enabling a patch manifest on a publisher game client preserves the publisher while overriding the version.
    /// </summary>
    [Fact]
    public void Construction_WithPublisherClientAndEnabledPatch_PreservesPublisherAndOverridesVersion()
    {
        // Arrange
        var profile = new GenHub.Core.Models.GameProfile.GameProfile
        {
            Id = "test-go-patch",
            Name = "Generals Online Patch Profile",
            GameClient = new GenHub.Core.Models.GameClients.GameClient
            {
                Id = "1.000104.generalsonline.gameclient.zerohour",
                Name = "Generals Online",
                PublisherType = "GeneralsOnline",
                GameType = GenHub.Core.Models.Enums.GameType.ZeroHour,
            },
            EnabledContentIds = ["1.106.communityoutpost.patch.zerohour"],
        };

        // Act
        var vm = new GameProfileItemViewModel("test-go-patch", profile, null!, null!);

        // Assert
        Assert.Equal("Generals Online", vm.Publisher);
        Assert.Equal("v1.06", vm.GameVersion);
    }

    /// <summary>
    /// Verifies that calling UpdateFromProfile updates version and publisher badges when the client changes.
    /// </summary>
    [Fact]
    public void UpdateFromProfile_WithChangedGameClient_UpdatesBadges()
    {
        // Arrange
        var initialProfile = new GenHub.Core.Models.GameProfile.GameProfile
        {
            Id = "test-profile-updating",
            Name = "Initial Profile",
        };

        var vm = new GameProfileItemViewModel("test-profile-updating", initialProfile, null!, null!);
        Assert.Empty(vm.GameVersion ?? string.Empty);

        var updatedClient = new GenHub.Core.Models.GameClients.GameClient
        {
            Id = "1.104.generalsonline.gameclient.zerohour",
            Name = "Generals Online",
            Version = "000104",
            PublisherType = "GeneralsOnline",
        };

        var updatedProfile = new GenHub.Core.Models.GameProfile.GameProfile
        {
            Id = "test-profile-updating",
            Name = "Updated Profile",
            GameClient = updatedClient,
        };

        // Act
        vm.UpdateFromProfile(updatedProfile);

        // Assert
        Assert.Equal("Generals Online", vm.Publisher);
        Assert.Equal("000104", vm.GameVersion);
        Assert.Contains("Generals Online", vm.Description);
    }

    /// <summary>
    /// Verifies that non-numeric version strings in manifest ID are preserved or formatted with v prefix.
    /// </summary>
    [Fact]
    public void Construction_WithNonNumericVersion_SetsVersionBadge()
    {
        // Arrange
        var profile = new GenHub.Core.Models.GameProfile.GameProfile
        {
            Id = "test-profile-nonnumeric",
            Name = "Test Non-Numeric Profile",
            EnabledContentIds = ["1.v104b.thesuperhackers.gameclient.zerohour"],
        };

        // Act
        var vm = new GameProfileItemViewModel("test-profile-nonnumeric", profile, null!, null!);

        // Assert
        Assert.Equal("The Super Hackers", vm.Publisher);
        Assert.Equal("v104b", vm.GameVersion);
    }

    /// <summary>
    /// Verifies that a Generals Online profile receives the non-retail compatible badge.
    /// </summary>
    [Fact]
    public void Construction_WithGeneralsOnlineClient_SetsNonRetailCompatibleBadge()
    {
        // Arrange
        var profile = new GenHub.Core.Models.GameProfile.GameProfile
        {
            Id = "test-go-compat",
            Name = "Generals Online Profile",
            GameClient = new GenHub.Core.Models.GameClients.GameClient
            {
                Id = "1.000104.generalsonline.gameclient.zerohour",
                Name = "Generals Online",
                PublisherType = "GeneralsOnline",
                GameType = GenHub.Core.Models.Enums.GameType.ZeroHour,
            },
        };

        // Act
        var vm = new GameProfileItemViewModel("test-go-compat", profile, null!, null!);

        // Assert
        Assert.True(vm.HasCompatibilityBadge);
        Assert.False(vm.IsRetailCompatible);
        Assert.Equal("Non-Retail Compatible", vm.CompatibilityBadgeText);
        Assert.Contains("1.04", vm.CompatibilityTooltip);
    }

    /// <summary>
    /// Verifies that a Community Patch non-retail profile receives the non-retail compatible badge.
    /// </summary>
    [Fact]
    public void Construction_WithNonRetailCommunityPatchClient_SetsNonRetailCompatibleBadge()
    {
        // Arrange
        var profile = new GenHub.Core.Models.GameProfile.GameProfile
        {
            Id = "test-cp-nonretail-compat",
            Name = "CP Non-Retail Profile",
            GameClient = new GenHub.Core.Models.GameClients.GameClient
            {
                Id = "1.106.communityoutpost.gameclient.zerohour.nonretail",
                Name = "Community Patch 1.06 (Non-Retail)",
                PublisherType = "CommunityOutpost",
                GameType = GenHub.Core.Models.Enums.GameType.ZeroHour,
            },
        };

        // Act
        var vm = new GameProfileItemViewModel("test-cp-nonretail-compat", profile, null!, null!);

        // Assert
        Assert.True(vm.HasCompatibilityBadge);
        Assert.False(vm.IsRetailCompatible);
        Assert.Equal("Non-Retail Compatible", vm.CompatibilityBadgeText);
    }

    /// <summary>
    /// Verifies that a Community Patch retail profile receives the retail compatible badge.
    /// </summary>
    [Fact]
    public void Construction_WithRetailCommunityPatchClient_SetsRetailCompatibleBadge()
    {
        // Arrange
        var profile = new GenHub.Core.Models.GameProfile.GameProfile
        {
            Id = "test-cp-retail-compat",
            Name = "CP Retail Profile",
            GameClient = new GenHub.Core.Models.GameClients.GameClient
            {
                Id = "1.106.communityoutpost.gameclient.zerohour.retail",
                Name = "Community Patch 1.06 (Retail)",
                PublisherType = "CommunityOutpost",
                GameType = GenHub.Core.Models.Enums.GameType.ZeroHour,
            },
        };

        // Act
        var vm = new GameProfileItemViewModel("test-cp-retail-compat", profile, null!, null!);

        // Assert
        Assert.True(vm.HasCompatibilityBadge);
        Assert.True(vm.IsRetailCompatible);
        Assert.Equal("Retail Compatible", vm.CompatibilityBadgeText);
        Assert.Contains("1.04", vm.CompatibilityTooltip);
    }

    /// <summary>
    /// Verifies that a retail Steam profile receives the retail compatible badge.
    /// </summary>
    [Fact]
    public void Construction_WithSteamClient_SetsRetailCompatibleBadge()
    {
        // Arrange
        var profile = new GenHub.Core.Models.GameProfile.GameProfile
        {
            Id = "test-steam-compat",
            Name = "Steam Retail Profile",
            GameClient = new GenHub.Core.Models.GameClients.GameClient
            {
                Id = "steam",
                Name = "Command & Conquer Generals Zero Hour (Steam)",
                PublisherType = "Steam",
                Version = "1.04",
                GameType = GenHub.Core.Models.Enums.GameType.ZeroHour,
            },
        };

        // Act
        var vm = new GameProfileItemViewModel("test-steam-compat", profile, null!, null!);

        // Assert
        Assert.True(vm.HasCompatibilityBadge);
        Assert.True(vm.IsRetailCompatible);
        Assert.Equal("Retail Compatible", vm.CompatibilityBadgeText);
    }

    /// <summary>
    /// Verifies that a profile without a GameClient has no compatibility badge.
    /// </summary>
    [Fact]
    public void Construction_WithoutGameClient_HasNoCompatibilityBadge()
    {
        // Arrange
        var profile = new GenHub.Core.Models.GameProfile.GameProfile
        {
            Id = "test-noclient-compat",
            Name = "No Client Profile",
            GameClient = null,
        };

        // Act
        var vm = new GameProfileItemViewModel("test-noclient-compat", profile, null!, null!);

        // Assert
        Assert.False(vm.HasCompatibilityBadge);
        Assert.Empty(vm.CompatibilityBadgeText ?? string.Empty);
    }

    /// <summary>
    /// Verifies that UpdateFromProfile updates the compatibility badge when client switches from retail to non-retail.
    /// </summary>
    [Fact]
    public void UpdateFromProfile_WhenClientChangesToNonRetail_UpdatesCompatibilityBadge()
    {
        // Arrange
        var initialProfile = new GenHub.Core.Models.GameProfile.GameProfile
        {
            Id = "test-updating-compat",
            Name = "Updating Profile",
            GameClient = new GenHub.Core.Models.GameClients.GameClient
            {
                Id = "steam",
                Name = "Steam Client",
                PublisherType = "Steam",
                Version = "1.04",
                GameType = GenHub.Core.Models.Enums.GameType.ZeroHour,
            },
        };

        var vm = new GameProfileItemViewModel("test-updating-compat", initialProfile, null!, null!);
        Assert.True(vm.HasCompatibilityBadge);
        Assert.True(vm.IsRetailCompatible);
        Assert.Equal("Retail Compatible", vm.CompatibilityBadgeText);

        var updatedProfile = new GenHub.Core.Models.GameProfile.GameProfile
        {
            Id = "test-updating-compat",
            Name = "Updating Profile",
            GameClient = new GenHub.Core.Models.GameClients.GameClient
            {
                Id = "1.000104.generalsonline.gameclient.zerohour",
                Name = "Generals Online",
                PublisherType = "GeneralsOnline",
                GameType = GenHub.Core.Models.Enums.GameType.ZeroHour,
            },
        };

        // Act
        vm.UpdateFromProfile(updatedProfile);

        // Assert
        Assert.True(vm.HasCompatibilityBadge);
        Assert.False(vm.IsRetailCompatible);
        Assert.Equal("Non-Retail Compatible", vm.CompatibilityBadgeText);
    }

    /// <summary>
    /// Verifies legacy PNG faction covers stored in profiles migrate to the re-encoded JPEG covers.
    /// </summary>
    /// <param name="legacyPath">The stored legacy path.</param>
    /// <param name="expectedPath">The expected migrated path.</param>
    [Theory]
    [InlineData("avares://GenHub/Assets/Covers/china-cover.png", "avares://GenHub/Assets/Covers/china-cover.jpg")]
    [InlineData("avares://GenHub/Assets/Covers/usa-cover.png", "avares://GenHub/Assets/Covers/usa-cover.jpg")]
    [InlineData("avares://GenHub/Assets/Covers/gla-cover.png", "avares://GenHub/Assets/Covers/gla-cover.jpg")]
    public void NormalizeCoverPath_LegacyPngCover_MigratesToJpeg(string legacyPath, string expectedPath)
    {
        Assert.Equal(expectedPath, GameProfileItemViewModel.NormalizeCoverPath(legacyPath));
    }

    /// <summary>
    /// Verifies legacy poster paths migrate to the re-encoded JPEG covers.
    /// </summary>
    [Fact]
    public void NormalizeCoverPath_LegacyPoster_MigratesToJpegCover()
    {
        Assert.Equal(
            "avares://GenHub/Assets/Covers/china-cover.jpg",
            GameProfileItemViewModel.NormalizeCoverPath("avares://GenHub/Assets/Images/china-poster.png"));
    }

    /// <summary>
    /// Verifies current JPEG covers and empty paths pass through unchanged.
    /// </summary>
    [Fact]
    public void NormalizeCoverPath_CurrentPaths_PassThroughUnchanged()
    {
        Assert.Equal(
            "avares://GenHub/Assets/Covers/usa-cover.jpg",
            GameProfileItemViewModel.NormalizeCoverPath("avares://GenHub/Assets/Covers/usa-cover.jpg"));
        Assert.Equal(string.Empty, GameProfileItemViewModel.NormalizeCoverPath(string.Empty));
    }

    /// <summary>
    /// Verifies that IsSteamIntegrationSupported evaluates both Steam installation and Windows binary format.
    /// </summary>
    [Fact]
    public void IsSteamIntegrationSupported_EvaluatesInstallationAndBinaryFormat()
    {
        // Steam installation with Windows executable
        var steamRetailProfile = new GenHub.Core.Models.GameProfile.GameProfile
        {
            Id = "test-steam-retail",
            Name = "Steam Retail",
            GameInstallationId = "installation.steam.zerohour",
            GameClient = new GenHub.Core.Models.GameClients.GameClient
            {
                Id = "steam",
                ExecutablePath = "generals.exe",
            },
        };

        var vmSteamRetail = new GameProfileItemViewModel("test-steam-retail", steamRetailProfile, null!, null!);
        Assert.True(vmSteamRetail.IsSteamInstallation);
        Assert.True(vmSteamRetail.IsSteamIntegrationSupported);

        // Steam installation with non-retail format
        var steamNonRetailProfile = new GenHub.Core.Models.GameProfile.GameProfile
        {
            Id = "test-steam-nonretail",
            Name = "Steam Non-Retail",
            GameInstallationId = "installation.steam.zerohour",
            GameClient = new GenHub.Core.Models.GameClients.GameClient
            {
                Id = "steam-flatpak",
                ExecutablePath = "com.fbraz3.GeneralsXZH.flatpakref",
            },
        };

        var vmSteamNonRetail = new GameProfileItemViewModel("test-steam-nonretail", steamNonRetailProfile, null!, null!);
        Assert.True(vmSteamNonRetail.IsSteamInstallation);
        Assert.False(vmSteamNonRetail.IsSteamIntegrationSupported);

        // Non-Steam installation
        var nonSteamProfile = new GenHub.Core.Models.GameProfile.GameProfile
        {
            Id = "test-ea-retail",
            Name = "EA Retail",
            GameInstallationId = "installation.ea.zerohour",
            GameClient = new GenHub.Core.Models.GameClients.GameClient
            {
                Id = "ea",
                ExecutablePath = "generals.exe",
            },
        };

        var vmNonSteam = new GameProfileItemViewModel("test-ea-retail", nonSteamProfile, null!, null!);
        Assert.False(vmNonSteam.IsSteamInstallation);
        Assert.False(vmNonSteam.IsSteamIntegrationSupported);
    }

    /// <summary>
    /// Verifies that setting IsSteamInstallation notifies property change for IsSteamIntegrationSupported.
    /// </summary>
    [Fact]
    public void SettingIsSteamInstallation_RaisesPropertyChangedForIsSteamIntegrationSupported()
    {
        var profile = new GenHub.Core.Models.GameProfile.GameProfile
        {
            Id = "test-notify",
            Name = "Test Notify",
            GameClient = new GenHub.Core.Models.GameClients.GameClient
            {
                Id = "test-client",
                ExecutablePath = "generals.exe",
            },
        };

        var vm = new GameProfileItemViewModel("test-notify", profile, null!, null!);
        var notifiedProperties = new List<string>();
        vm.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName != null)
            {
                notifiedProperties.Add(args.PropertyName);
            }
        };

        vm.IsSteamInstallation = true;

        Assert.Contains(nameof(GameProfileItemViewModel.IsSteamInstallation), notifiedProperties);
        Assert.Contains(nameof(GameProfileItemViewModel.IsSteamIntegrationSupported), notifiedProperties);
    }
}
