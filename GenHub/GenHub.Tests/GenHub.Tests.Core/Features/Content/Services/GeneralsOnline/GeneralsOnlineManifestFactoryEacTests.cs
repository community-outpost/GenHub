using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services.GeneralsOnline;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Tests covering the Easy Anti-Cheat era layout of the Generals Online portable,
/// where <c>EAC_LaunchGeneralsOnline.exe</c> wraps the game binary named by
/// <c>EasyAntiCheat/Settings.json</c>.
/// </summary>
public class GeneralsOnlineManifestFactoryEacTests : IDisposable
{
    private readonly string _extractedDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralsOnlineManifestFactoryEacTests"/> class.
    /// </summary>
    public GeneralsOnlineManifestFactoryEacTests()
    {
        _extractedDirectory = Path.Combine(Path.GetTempPath(), $"genhub-eac-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_extractedDirectory);
    }

    /// <summary>
    /// The EAC bootstrapper is the launch target, so it must be the file carrying
    /// <see cref="ManifestFile.IsExecutable"/> in the game client manifest.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_EacLayout_MarksWrapperAsExecutableAsync()
    {
        WriteEacPortableLayout();

        var gameClient = await CreateGameClientManifestAsync();

        var executables = gameClient.Files.Where(file => file.IsExecutable).ToList();
        var executable = Assert.Single(executables);
        Assert.Equal(
            GameClientConstants.GeneralsOnlineEacLauncherExecutable,
            Path.GetFileName(executable.RelativePath),
            ignoreCase: true);
    }

    /// <summary>
    /// Easy Anti-Cheat launches the binary named by its settings file, so the wrapped
    /// game binary must remain in the workspace as a non-launch file. Dropping it
    /// leaves the bootstrapper with nothing to start.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_EacLayout_RetainsWrappedBinaryAsWorkspaceFileAsync()
    {
        WriteEacPortableLayout();

        var gameClient = await CreateGameClientManifestAsync();

        var wrapped = gameClient.Files.SingleOrDefault(file =>
            Path.GetFileName(file.RelativePath)
                .Equals(GameClientConstants.GeneralsOnline60HzExecutable, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(wrapped);
        Assert.False(wrapped!.IsExecutable);
    }

    /// <summary>
    /// The portable also ships a non-60Hz binary. Easy Anti-Cheat wraps only the binary named
    /// by its settings file, so the other one stays as plain workspace content.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_EacLayout_RetainsDefaultBinaryAsWorkspaceFileAsync()
    {
        WriteEacPortableLayout();

        var gameClient = await CreateGameClientManifestAsync();

        var defaultBinary = gameClient.Files.SingleOrDefault(file =>
            Path.GetFileName(file.RelativePath)
                .Equals(GameClientConstants.GeneralsOnlineDefaultExecutable, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(defaultBinary);
        Assert.False(defaultBinary!.IsExecutable);
    }

    /// <summary>
    /// Only the bootstrapper at the archive root is the supported entry point. A nested file
    /// that merely shares its name must not divert the launch target away from the real client.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_NestedWrapperName_DoesNotBecomeLaunchTargetAsync()
    {
        WriteFile(GameClientConstants.GeneralsOnline60HzExecutable);
        WriteFile(Path.Combine("tools", GameClientConstants.GeneralsOnlineEacLauncherExecutable));

        var gameClient = await CreateGameClientManifestAsync();

        var executables = gameClient.Files.Where(file => file.IsExecutable).ToList();
        var executable = Assert.Single(executables);
        Assert.Equal(
            GameClientConstants.GeneralsOnline60HzExecutable,
            executable.RelativePath,
            ignoreCase: true);
    }

    /// <summary>
    /// Pre-EAC portables ship no bootstrapper, so the 60Hz binary stays the launch target.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_PreEacLayout_MarksSixtyHertzBinaryAsExecutableAsync()
    {
        WriteFile(GameClientConstants.GeneralsOnline60HzExecutable);
        WriteFile("libcurl.dll");

        var gameClient = await CreateGameClientManifestAsync();

        var executables = gameClient.Files.Where(file => file.IsExecutable).ToList();
        var executable = Assert.Single(executables);
        Assert.Equal(
            GameClientConstants.GeneralsOnline60HzExecutable,
            Path.GetFileName(executable.RelativePath),
            ignoreCase: true);
    }

    /// <summary>
    /// Verifies that EAC portable layout configures a post-install step to run the verified EAC setup executable.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_EacLayout_ConfiguresEacPostInstallStepAsync()
    {
        WriteEacPortableLayout();

        var gameClient = await CreateGameClientManifestAsync();

        Assert.NotNull(gameClient.InstallationInstructions);
        var postSteps = gameClient.InstallationInstructions.PostInstallSteps;
        var eacStep = Assert.Single(postSteps);

        Assert.Equal(GeneralsOnlineConstants.EacStepName, eacStep.Name);
        Assert.Equal(InstallationStepKind.RunVerifiedInstaller, eacStep.Kind);
        Assert.Equal(GameClientConstants.GeneralsOnlineEacSetupExecutable, eacStep.TargetRelativePath);
        Assert.True(eacStep.RequiresElevation);
        Assert.True(eacStep.RunOnce);
        Assert.Equal(GeneralsOnlineConstants.EacStepKey, eacStep.StepKey);
        Assert.Equal(GeneralsOnlineConstants.EacStatusMessage, eacStep.StatusMessage);
        Assert.NotNull(eacStep.Arguments);
        Assert.Equal(
            [GeneralsOnlineConstants.EacInstallCommand, GeneralsOnlineConstants.EacProductId],
            eacStep.Arguments);
    }

    /// <summary>
    /// Verifies that Pre-EAC portable layout does not configure an EAC post-install step when setup executable is absent.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_PreEacLayout_DoesNotConfigureEacPostInstallStepAsync()
    {
        WriteFile(GameClientConstants.GeneralsOnline60HzExecutable);
        WriteFile("libcurl.dll");

        var gameClient = await CreateGameClientManifestAsync();

        Assert.NotNull(gameClient.InstallationInstructions);
        var eacStep = gameClient.InstallationInstructions.PostInstallSteps.FirstOrDefault(s =>
            string.Equals(s.TargetRelativePath, GameClientConstants.GeneralsOnlineEacSetupExecutable, StringComparison.OrdinalIgnoreCase));
        Assert.Null(eacStep);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_extractedDirectory))
        {
            Directory.Delete(_extractedDirectory, recursive: true);
        }
    }

    private static ContentManifest CreateOriginalManifest() => new()
    {
        Id = "1.605261.generalsonline.gameclient.60hz",
        Name = "GeneralsOnline",
        Version = "060526_QFE1",
        ContentType = ContentType.GameClient,
        TargetGame = GameType.ZeroHour,
        Publisher = new PublisherInfo
        {
            Name = "GeneralsOnline",
            PublisherType = PublisherTypeConstants.GeneralsOnline,
        },
    };

    private void WriteEacPortableLayout()
    {
        WriteFile(GameClientConstants.GeneralsOnlineEacLauncherExecutable);
        WriteFile(GameClientConstants.GeneralsOnlineEacSetupExecutable);
        WriteFile(GameClientConstants.GeneralsOnline60HzExecutable);
        WriteFile(GameClientConstants.GeneralsOnlineDefaultExecutable);
        WriteFile(Path.Combine("EasyAntiCheat", "Settings.json"));
        WriteFile("EOSSDK-Win32-Shipping.dll");
    }

    private void WriteFile(string relativePath)
    {
        var fullPath = Path.Combine(_extractedDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, relativePath);
    }

    private async Task<ContentManifest> CreateGameClientManifestAsync()
    {
        var providerLoader = new Mock<IProviderDefinitionLoader>();
        var factory = new GeneralsOnlineManifestFactory(
            NullLogger<GeneralsOnlineManifestFactory>.Instance,
            providerLoader.Object);

        var manifests = await factory.CreateManifestsFromExtractedContentAsync(
            CreateOriginalManifest(),
            _extractedDirectory);

        return manifests.Single(manifest => manifest.ContentType == ContentType.GameClient);
    }
}
