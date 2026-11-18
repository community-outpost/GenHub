using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Features.GameProfiles.ViewModels;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels;

/// <summary>
/// Regression tests for read-only library metadata and runtime launch captions.
/// </summary>
public class GameProfileLibraryDetailsTests
{
    /// <summary>
    /// Verifies that refreshing a profile replaces the library's saved data snapshot.
    /// </summary>
    [Fact]
    public void UpdateFromProfile_RefreshesSavedLibraryDetails()
    {
        var original = CreateProfile("Original description", "-window");
        var item = CreateItem(original);
        var refreshed = CreateProfile("Updated description", "-quickstart");
        refreshed.GameClient!.Name = "Updated game name";

        item.UpdateFromProfile(refreshed);

        Assert.Same(refreshed, item.LibraryProfile);
        Assert.Same(original, item.Profile);
        Assert.Equal("Updated description", item.LibraryDescription);
        Assert.True(item.HasLibraryDescription);
        Assert.Equal("Updated game name", item.LibraryGameName);
        Assert.Equal("Updated game name · v1.04", item.LibraryHeaderMetadata);
        Assert.Equal("-quickstart", item.LibraryArguments);
    }

    /// <summary>
    /// Verifies that refreshing a Steam profile applies its saved launch method without resetting runtime state.
    /// </summary>
    [Fact]
    public void UpdateFromProfile_RefreshesSavedSteamLaunchSetting()
    {
        var original = CreateProfile(string.Empty, string.Empty);
        original.UseSteamLaunch = false;
        var item = CreateItem(original);
        item.InstallationType = GameInstallationType.Steam;
        var localLaunch = item.LibraryLaunchMethod;
        item.IsProcessRunning = true;
        var refreshed = CreateProfile(string.Empty, string.Empty);
        refreshed.UseSteamLaunch = true;

        item.UpdateFromProfile(refreshed);

        Assert.True(item.UseSteamLaunch);
        Assert.NotEqual(localLaunch, item.LibraryLaunchMethod);
        Assert.True(item.IsProcessRunning);
    }

    /// <summary>
    /// Verifies that a custom description takes precedence over derived launch metadata.
    /// </summary>
    [Fact]
    public void LibraryDescription_PreservesCustomDescription()
    {
        var item = CreateItem(CreateProfile("My LAN configuration", string.Empty));
        item.Description = "Derived game and publisher metadata";

        Assert.Equal("My LAN configuration", item.LibraryDescription);
    }

    /// <summary>
    /// Verifies that generated launch metadata does not fill an empty description with repeated labels.
    /// </summary>
    [Fact]
    public void LibraryDescription_DoesNotRepeatGeneratedGameAndVersionMetadata()
    {
        var item = CreateItem(CreateProfile(string.Empty, string.Empty));
        item.Description = "PC · v1.04 · Zero Hour";

        Assert.False(item.HasLibraryDescription);
        Assert.NotEqual(item.Description, item.LibraryDescription);
    }

    /// <summary>
    /// Verifies that runtime changes notify status and launch mode bindings.
    /// </summary>
    [Fact]
    public void RuntimeStateChanges_RefreshLibraryCaptions()
    {
        var item = CreateItem(CreateProfile(string.Empty, string.Empty));
        var properties = new List<string?>();
        item.PropertyChanged += (_, e) => properties.Add(e.PropertyName);
        var available = item.LibraryLaunchStatus;
        var localLaunch = item.LibraryLaunchMethod;

        item.IsPreparingWorkspace = true;
        var preparing = item.LibraryLaunchStatus;
        item.IsProcessRunning = true;
        var running = item.LibraryLaunchStatus;
        item.UseSteamLaunch = true;

        Assert.NotEqual(available, preparing);
        Assert.NotEqual(preparing, running);
        Assert.NotEqual(localLaunch, item.LibraryLaunchMethod);
        Assert.Equal(2, properties.Count(p => p == nameof(item.LibraryLaunchStatus)));
        Assert.Contains(nameof(item.LibraryLaunchMethod), properties);
    }

    /// <summary>
    /// Verifies that explicitly suppressed versions remain hidden in the library.
    /// </summary>
    [Fact]
    public void LibraryVersion_DoesNotRestoreSuppressedLocalVersion()
    {
        var profile = CreateProfile(string.Empty, string.Empty);
        profile.GameClient!.Id = "schema.1.local.map.local-map";
        profile.GameClient.Version = "1.0";
        var item = CreateItem(profile);

        Assert.Empty(item.GameVersion ?? string.Empty);
        Assert.Empty(item.LibraryVersion);
    }

    /// <summary>
    /// Verifies that the header identifies custom profiles without repeating the default game title.
    /// </summary>
    /// <param name="profileName">The profile title.</param>
    /// <param name="gameName">The underlying game name.</param>
    /// <param name="version">The already formatted display version.</param>
    /// <param name="expected">The compact metadata caption.</param>
    [Theory]
    [InlineData("Zero Hour", "Zero Hour", "v1.04", "v1.04")]
    [InlineData(" zero HOUR ", "Zero Hour", "v1.04", "v1.04")]
    [InlineData("Zero Hour · Community Patch", "Zero Hour", "v1.04", "v1.04")]
    [InlineData("Zero Hour - LAN", "Zero Hour", "v1.04", "v1.04")]
    [InlineData("Zero Hourglass", "Zero Hour", "v1.04", "Zero Hour · v1.04")]
    [InlineData("LAN night", "Zero Hour", "v1.04", "Zero Hour · v1.04")]
    [InlineData("LAN night", "Zero Hour", "", "Zero Hour")]
    [InlineData("Zero Hour", "Zero Hour", "", "")]
    [InlineData("Генералы", "Генералы", "v1.08", "v1.08")]
    public void LibraryHeaderMetadata_ShowsOnlyDistinctInformation(string profileName, string gameName, string version, string expected)
    {
        var profile = CreateProfile(string.Empty, string.Empty);
        profile.Name = profileName;
        profile.GameClient!.Name = gameName;
        var item = CreateItem(profile);
        item.GameVersion = version;

        Assert.Equal(expected, item.LibraryHeaderMetadata);
    }

    /// <summary>
    /// Verifies that renaming a profile and changing its display version refresh the compact header.
    /// </summary>
    [Fact]
    public void LibraryHeaderMetadata_RefreshesAfterRenameAndVersionChange()
    {
        var item = CreateItem(CreateProfile(string.Empty, string.Empty));
        var properties = new List<string?>();
        item.PropertyChanged += (_, e) => properties.Add(e.PropertyName);

        item.Name = "Zero Hour";
        Assert.Equal("v1.04", item.LibraryHeaderMetadata);
        Assert.Contains(nameof(item.LibraryHeaderMetadata), properties);

        properties.Clear();
        item.Name = "LAN night";
        Assert.Equal("Zero Hour · v1.04", item.LibraryHeaderMetadata);
        Assert.Contains(nameof(item.LibraryHeaderMetadata), properties);

        properties.Clear();
        item.GameVersion = "v1.05";
        Assert.Equal("Zero Hour · v1.05", item.LibraryHeaderMetadata);
        Assert.Contains(nameof(item.LibraryHeaderMetadata), properties);
    }

    private static GameProfile CreateProfile(string description, string arguments)
    {
        return new GameProfile
        {
            Id = "library-details-test",
            Name = "My profile",
            Description = description,
            CommandLineArguments = arguments,
            GameClient = new GameClient
            {
                Id = "1.104.steam.gameclient.zerohour",
                Name = "Zero Hour",
                Version = "1.04",
                GameType = GameType.ZeroHour,
                PublisherType = PublisherTypeConstants.Steam,
            },
        };
    }

    private static GameProfileItemViewModel CreateItem(GameProfile profile)
    {
        return new GameProfileItemViewModel(profile.Id, profile, UriConstants.ZeroHourIconUri, string.Empty);
    }
}
