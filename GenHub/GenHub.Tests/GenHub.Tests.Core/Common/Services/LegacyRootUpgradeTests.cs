using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Common.Services;

/// <summary>
/// Covers the first launch after upgrading from a release that kept its data under the roaming
/// profile.
/// <para>
/// <see cref="UserSettingsService"/> loads in its own constructor and resolves the settings path
/// straight from <see cref="IAppConfiguration"/>, so it runs before
/// <see cref="ConfigurationProviderService"/> has had any chance to migrate the legacy root. Left
/// alone it would start from defaults, and the first save of the session would then write those
/// defaults over the freshly migrated settings file, permanently destroying the user's settings.
/// </para>
/// </summary>
public class LegacyRootUpgradeTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _legacyRoot;
    private readonly string _newRoot;

    /// <summary>
    /// Initializes a new instance of the <see cref="LegacyRootUpgradeTests"/> class.
    /// </summary>
    public LegacyRootUpgradeTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"genhub-upgrade-{Guid.NewGuid():N}");
        _legacyRoot = Path.Combine(_testRoot, "roaming");
        _newRoot = Path.Combine(_testRoot, "local");
        Directory.CreateDirectory(_legacyRoot);
        Directory.CreateDirectory(_newRoot);
    }

    /// <summary>
    /// Removes the temporary roots created for the test.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Verifies that the settings a user had before the upgrade are in effect on the first launch,
    /// without waiting for a restart.
    /// </summary>
    [Fact]
    public void FirstLaunch_WithLegacySettings_LoadsLegacyValues()
    {
        WriteLegacySettings("""
        {
          "theme": "Light",
          "maxConcurrentDownloads": 7,
          "defaultWorkspaceStrategy": "SymlinkOnly"
        }
        """);

        var settings = CreateSettingsService().Get();

        Assert.Equal("Light", settings.Theme);
        Assert.Equal(7, settings.MaxConcurrentDownloads);
        Assert.Equal(WorkspaceStrategy.SymlinkOnly, settings.DefaultWorkspaceStrategy);
    }

    /// <summary>
    /// Verifies the exact sequence that destroyed user settings: a first-launch load, the legacy
    /// root migration moving the settings file into the new root, and then a save during that same
    /// session. The saved file must still carry the user's values, not defaults.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FirstLaunch_ThenMigrationThenSave_PreservesLegacyValuesAsync()
    {
        WriteLegacySettings("""
        {
          "theme": "Light",
          "maxConcurrentDownloads": 7
        }
        """);

        var appConfig = CreateAppConfig();
        var settingsService = CreateSettingsService(appConfig);
        var provider = new ConfigurationProviderService(
            appConfig,
            settingsService,
            Mock.Of<ILogger<ConfigurationProviderService>>());

        // Triggers the legacy root migration, which moves settings.json into the new root.
        provider.GetApplicationDataPath();
        Assert.True(File.Exists(Path.Combine(_newRoot, FileTypes.SettingsFileName)));

        settingsService.Update(settings => settings.WindowWidth = 1440.0);
        await settingsService.SaveAsync();

        var persisted = CreateSettingsService(appConfig).Get();
        Assert.Equal("Light", persisted.Theme);
        Assert.Equal(7, persisted.MaxConcurrentDownloads);
        Assert.Equal(1440.0, persisted.WindowWidth);
    }

    /// <summary>
    /// Verifies that an application data path override carried over from the legacy settings is
    /// honored on the first launch rather than after a restart.
    /// </summary>
    [Fact]
    public void FirstLaunch_WithLegacyApplicationDataPathOverride_HonorsOverride()
    {
        var overridePath = Path.Combine(_testRoot, "relocated");
        Directory.CreateDirectory(overridePath);
        var escapedOverridePath = overridePath.Replace("\\", "\\\\");
        WriteLegacySettings($$"""
        {
          "applicationDataPath": "{{escapedOverridePath}}"
        }
        """);

        var appConfig = CreateAppConfig();
        var provider = new ConfigurationProviderService(
            appConfig,
            CreateSettingsService(appConfig),
            Mock.Of<ILogger<ConfigurationProviderService>>());

        Assert.Equal(overridePath, provider.GetApplicationDataPath());
        Assert.Equal(Path.Combine(overridePath, DirectoryNames.Profiles), provider.GetProfilesPath());
        Assert.Equal(Path.Combine(overridePath, FileTypes.ManifestsDirectory), provider.GetManifestsPath());
    }

    /// <summary>
    /// Verifies that the migration puts the profiles where <see cref="ConfigurationProviderService.GetProfilesPath"/>
    /// resolves them when an application data path override is in effect, rather than in the
    /// configured root the app would never look at.
    /// </summary>
    [Fact]
    public void FirstLaunch_WithOverride_MigratesDataIntoTheRootTheAppReadsFrom()
    {
        var overridePath = Path.Combine(_testRoot, "relocated");
        WriteLegacySettings($$"""
        {
          "applicationDataPath": "{{overridePath.Replace("\\", "\\\\")}}"
        }
        """);
        SeedLegacyDataDirectories();

        var appConfig = CreateAppConfig();
        var provider = new ConfigurationProviderService(
            appConfig,
            CreateSettingsService(appConfig),
            Mock.Of<ILogger<ConfigurationProviderService>>());

        Assert.Equal("profile", File.ReadAllText(Path.Combine(provider.GetProfilesPath(), "profile.json")));
        Assert.Equal("manifest", File.ReadAllText(Path.Combine(provider.GetManifestsPath(), "content.manifest.json")));
        Assert.Equal("workspaces", File.ReadAllText(Path.Combine(provider.GetApplicationDataPath(), FileTypes.WorkspaceMetadataFileName)));

        Assert.False(Directory.Exists(Path.Combine(_newRoot, DirectoryNames.Profiles)));
        Assert.True(File.Exists(Path.Combine(_newRoot, FileTypes.SettingsFileName)));
    }

    /// <summary>
    /// Verifies that the settings file releases up to v0.0.3 wrote, which was named after the JSON
    /// extension rather than the settings file name, is still picked up on the first launch.
    /// </summary>
    [Fact]
    public void FirstLaunch_WithV003SettingsFileName_LoadsLegacyValues()
    {
        var legacyJson = """
        { "theme": "Light", "maxConcurrentDownloads": 7 }
        """;
        File.WriteAllText(Path.Combine(_legacyRoot, FileTypes.LegacySettingsFileName), legacyJson);

        var settings = CreateSettingsService().Get();

        Assert.Equal("Light", settings.Theme);
        Assert.Equal(7, settings.MaxConcurrentDownloads);
    }

    /// <summary>
    /// Verifies that a normalization failure, which used to reset the settings to defaults while the
    /// settings path still pointed at the user's file, keeps the loaded values instead.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FirstLaunch_WhenNormalizationThrows_KeepsLoadedValuesAsync()
    {
        WriteLegacySettings("""
        { "theme": "Light", "maxConcurrentDownloads": 7 }
        """);

        var appConfig = CreateAppConfigMock();
        appConfig.Setup(config => config.GetMinConcurrentDownloads()).Returns(8);
        appConfig.Setup(config => config.GetMaxConcurrentDownloads()).Returns(1);

        var service = new UserSettingsService(Mock.Of<ILogger<UserSettingsService>>(), appConfig.Object);
        Assert.Equal("Light", service.Get().Theme);

        await service.SaveAsync();

        Assert.Equal("Light", CreateSettingsService().Get().Theme);
    }

    /// <summary>
    /// Verifies that a failed initialization can never persist defaults over a settings file that was
    /// never read successfully.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Save_AfterFailedInitialization_RefusesToOverwriteExistingSettingsAsync()
    {
        var settingsPath = Path.Combine(_newRoot, FileTypes.SettingsFileName);
        var existingJson = """
        { "theme": "Light" }
        """;
        File.WriteAllText(settingsPath, existingJson);

        var appConfig = CreateBaseAppConfigMock();
        appConfig.Setup(config => config.GetConfiguredDataPath()).Throws(new UnauthorizedAccessException("denied"));

        var service = new UserSettingsService(Mock.Of<ILogger<UserSettingsService>>(), appConfig.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync());
        Assert.Contains("Light", File.ReadAllText(settingsPath));
    }

    /// <summary>
    /// Verifies that a settings file already present in the current root wins over the legacy copy.
    /// </summary>
    [Fact]
    public void SecondLaunch_WithSettingsInNewRoot_IgnoresLegacyFile()
    {
        WriteLegacySettings("""
        { "theme": "Light" }
        """);
        var currentSettingsJson = """
        { "theme": "Dark" }
        """;
        File.WriteAllText(Path.Combine(_newRoot, FileTypes.SettingsFileName), currentSettingsJson);

        var settings = CreateSettingsService().Get();

        Assert.Equal("Dark", settings.Theme);
    }

    /// <summary>
    /// Verifies that a fresh install, which has no legacy root at all, is unaffected.
    /// </summary>
    [Fact]
    public void FreshInstall_WithoutLegacyRoot_UsesDefaults()
    {
        Directory.Delete(_legacyRoot);

        var service = CreateSettingsService();
        var settings = service.Get();

        Assert.Equal(AppConstants.DefaultThemeName, settings.Theme);
        Assert.False(settings.IsExplicitlySet(nameof(UserSettings.ApplicationDataPath)));
        Assert.False(File.Exists(Path.Combine(_newRoot, FileTypes.SettingsFileName)));
    }

    /// <summary>
    /// Verifies that a failure while looking for the pre-upgrade settings cannot stop startup.
    /// </summary>
    [Fact]
    public void FirstLaunch_WhenLegacyLookupThrows_FallsBackToDefaults()
    {
        var appConfig = CreateAppConfigMock();
        appConfig.Setup(config => config.GetLegacyConfiguredDataPath()).Throws(new UnauthorizedAccessException("denied"));

        var service = new UserSettingsService(Mock.Of<ILogger<UserSettingsService>>(), appConfig.Object);

        Assert.Equal(AppConstants.DefaultThemeName, service.Get().Theme);
    }

    private static Mock<IAppConfiguration> CreateBaseAppConfigMock()
    {
        var appConfig = new Mock<IAppConfiguration>();
        appConfig.Setup(config => config.GetMinConcurrentDownloads()).Returns(1);
        appConfig.Setup(config => config.GetMaxConcurrentDownloads()).Returns(8);
        appConfig.Setup(config => config.GetMinDownloadTimeoutSeconds()).Returns(30);
        appConfig.Setup(config => config.GetMaxDownloadTimeoutSeconds()).Returns(600);
        appConfig.Setup(config => config.GetMinDownloadBufferSizeBytes()).Returns(4096);
        appConfig.Setup(config => config.GetMaxDownloadBufferSizeBytes()).Returns(1048576);
        return appConfig;
    }

    private Mock<IAppConfiguration> CreateAppConfigMock()
    {
        var appConfig = CreateBaseAppConfigMock();
        appConfig.Setup(config => config.GetConfiguredDataPath()).Returns(_newRoot);
        appConfig.Setup(config => config.GetLegacyConfiguredDataPath()).Returns(_legacyRoot);
        return appConfig;
    }

    private IAppConfiguration CreateAppConfig() => CreateAppConfigMock().Object;

    private UserSettingsService CreateSettingsService(IAppConfiguration? appConfig = null) =>
        new(Mock.Of<ILogger<UserSettingsService>>(), appConfig ?? CreateAppConfig());

    private void WriteLegacySettings(string json) =>
        File.WriteAllText(Path.Combine(_legacyRoot, FileTypes.SettingsFileName), json);

    private void SeedLegacyDataDirectories()
    {
        WriteLegacyFile(Path.Combine(_legacyRoot, DirectoryNames.Profiles, "profile.json"), "profile");
        WriteLegacyFile(Path.Combine(_legacyRoot, FileTypes.ManifestsDirectory, "content.manifest.json"), "manifest");
        WriteLegacyFile(Path.Combine(_legacyRoot, FileTypes.WorkspaceMetadataFileName), "workspaces");
    }

    private void WriteLegacyFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
