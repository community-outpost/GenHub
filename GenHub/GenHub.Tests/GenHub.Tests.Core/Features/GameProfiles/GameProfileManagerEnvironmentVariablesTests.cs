using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.UserData;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Features.GameProfiles.Infrastructure;
using GenHub.Features.GameProfiles.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.Features.GameProfiles;

/// <summary>
/// Verifies that <see cref="GameProfileManager.UpdateProfileAsync"/> persists
/// <see cref="UpdateProfileRequest.EnvironmentVariables"/> through the file-backed repository.
/// </summary>
public sealed class GameProfileManagerEnvironmentVariablesTests : IDisposable
{
    private const string ProfileId = "env-vars-profile";
    private const string ExistingVariableName = "EXISTING_VARIABLE";
    private const string ExistingVariableValue = "existing";
    private const string RequestedVariableName = "REQUESTED_VARIABLE";
    private const string RequestedVariableValue = "requested";
    private const string UpdatedCommandLineArguments = "-quickstart";

    private readonly string _profilesDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileManagerEnvironmentVariablesTests"/> class.
    /// </summary>
    public GameProfileManagerEnvironmentVariablesTests()
    {
        _profilesDir = Directory.CreateTempSubdirectory("GenHub.ProfileEnvVars.").FullName;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_profilesDir))
        {
            Directory.Delete(_profilesDir, true);
        }
    }

    /// <summary>
    /// Environment variables set on the update request replace the profile's variables and survive a reload.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_Should_PersistEnvironmentVariables_When_SetAsync()
    {
        await SaveExistingProfileAsync();
        var request = new UpdateProfileRequest
        {
            EnvironmentVariables = new Dictionary<string, string> { [RequestedVariableName] = RequestedVariableValue },
        };

        var result = await CreateProfileManager().UpdateProfileAsync(ProfileId, request);

        Assert.True(result.Success, result.FirstError);
        var reloaded = await ReloadProfileAsync();
        var variable = Assert.Single(reloaded.EnvironmentVariables);
        Assert.Equal(RequestedVariableName, variable.Key);
        Assert.Equal(RequestedVariableValue, variable.Value);
    }

    /// <summary>
    /// A null environment variable map leaves the profile's variables unchanged.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_Should_PreserveEnvironmentVariables_When_OmittedAsync()
    {
        await SaveExistingProfileAsync();
        var request = new UpdateProfileRequest { CommandLineArguments = UpdatedCommandLineArguments };

        var result = await CreateProfileManager().UpdateProfileAsync(ProfileId, request);

        Assert.True(result.Success, result.FirstError);
        var reloaded = await ReloadProfileAsync();
        Assert.Equal(UpdatedCommandLineArguments, reloaded.CommandLineArguments);
        var variable = Assert.Single(reloaded.EnvironmentVariables);
        Assert.Equal(ExistingVariableName, variable.Key);
        Assert.Equal(ExistingVariableValue, variable.Value);
    }

    /// <summary>
    /// An empty environment variable map clears the profile's variables, matching launch arguments.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task UpdateProfileAsync_Should_ClearEnvironmentVariables_When_EmptyAsync()
    {
        await SaveExistingProfileAsync();
        var request = new UpdateProfileRequest { EnvironmentVariables = [] };

        var result = await CreateProfileManager().UpdateProfileAsync(ProfileId, request);

        Assert.True(result.Success, result.FirstError);
        var reloaded = await ReloadProfileAsync();
        Assert.Empty(reloaded.EnvironmentVariables);
    }

    private GameProfileRepository CreateRepository() =>
        new(_profilesDir, NullLogger<GameProfileRepository>.Instance);

    private GameProfileManager CreateProfileManager() =>
        new(
            CreateRepository(),
            new Mock<IGameInstallationService>().Object,
            new Mock<IContentManifestPool>().Object,
            new Mock<IGameSettingsService>().Object,
            new Mock<IWorkspaceManager>().Object,
            new Mock<IProfileContentLinker>().Object,
            NullLogger<GameProfileManager>.Instance);

    private async Task SaveExistingProfileAsync()
    {
        var profile = new GameProfile
        {
            Id = ProfileId,
            Name = "Environment Profile",
            GameInstallationId = "install-1",
            GameClient = new GameClient { Id = "client-1", Version = "1.0" },
            EnvironmentVariables = new Dictionary<string, string> { [ExistingVariableName] = ExistingVariableValue },
        };

        var saveResult = await CreateRepository().SaveProfileAsync(profile);
        Assert.True(saveResult.Success, saveResult.FirstError);
    }

    private async Task<GameProfile> ReloadProfileAsync()
    {
        var loadResult = await CreateRepository().LoadProfileAsync(ProfileId);
        Assert.True(loadResult.Success, loadResult.FirstError);
        Assert.NotNull(loadResult.Data);
        return loadResult.Data;
    }
}
