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
        original.EnabledContentIds.Add("original-content");
        var item = CreateItem(original);
        var refreshed = CreateProfile("Updated description", "-quickstart");
        refreshed.GameClient!.Name = "Updated game name";
        refreshed.EnabledContentIds.AddRange(["first-content", "second-content"]);

        item.UpdateFromProfile(refreshed);

        Assert.Same(refreshed, item.LibraryProfile);
        Assert.Same(original, item.Profile);
        Assert.Equal("Updated description", item.LibraryDescription);
        Assert.Equal("Updated game name", item.LibraryGameName);
        Assert.Equal("-quickstart", item.LibraryArguments);
        Assert.Equal(2, item.LibraryContentCount);
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
