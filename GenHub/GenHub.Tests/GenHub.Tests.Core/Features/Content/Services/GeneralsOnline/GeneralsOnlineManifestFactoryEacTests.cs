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
        Assert.Null(eacStep.StepKey);
        Assert.Equal(GeneralsOnlineConstants.EacStatusMessage, eacStep.StatusMessage);
        Assert.NotNull(eacStep.Arguments);
        Assert.Equal(
            [GeneralsOnlineConstants.EacInstallCommand, string.Empty],
            eacStep.Arguments);

        var binding = Assert.Single(eacStep.ArgumentBindings ?? []);
        Assert.Equal(1, binding.ArgumentIndex);
        Assert.Equal(ManifestConstants.InstallationBindingJsonSource, binding.Source);
        Assert.Equal(GeneralsOnlineConstants.EacSettingsRelativePath, binding.RelativePath);
        Assert.Equal(GeneralsOnlineConstants.EacSettingsProductIdKey, binding.Key);
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

    /// <summary>
    /// Verifies that an inherited EAC step is not duplicated when EAC portable layout already contains the setup executable.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_InheritedEacStep_SetupExecutablePresent_DoesNotDuplicateEacStepAsync()
    {
        WriteEacPortableLayout();

        var originalManifest = CreateOriginalManifest();
        originalManifest.InstallationInstructions = new InstallationInstructions
        {
            PostInstallSteps =
            [
                new InstallationStep
                {
                    Name = GeneralsOnlineConstants.EacStepName,
                    Kind = InstallationStepKind.RunVerifiedInstaller,
                    TargetRelativePath = GameClientConstants.GeneralsOnlineEacSetupExecutable,
                },
            ],
        };

        var gameClient = await CreateGameClientManifestAsync(originalManifest);

        Assert.NotNull(gameClient.InstallationInstructions);
        var eacSteps = gameClient.InstallationInstructions.PostInstallSteps.Where(s =>
            string.Equals(s.TargetRelativePath, GameClientConstants.GeneralsOnlineEacSetupExecutable, StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(eacSteps);
    }

    /// <summary>
    /// Verifies that an inherited EAC step is dropped when the setup executable is absent in extracted content.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_InheritedEacStep_SetupExecutableAbsent_DropsEacStepAsync()
    {
        WriteFile(GameClientConstants.GeneralsOnline60HzExecutable);
        WriteFile("libcurl.dll");

        var originalManifest = CreateOriginalManifest();
        originalManifest.InstallationInstructions = new InstallationInstructions
        {
            PostInstallSteps =
            [
                new InstallationStep
                {
                    Name = GeneralsOnlineConstants.EacStepName,
                    Kind = InstallationStepKind.RunVerifiedInstaller,
                    TargetRelativePath = GameClientConstants.GeneralsOnlineEacSetupExecutable,
                },
            ],
        };

        var gameClient = await CreateGameClientManifestAsync(originalManifest);

        Assert.NotNull(gameClient.InstallationInstructions);
        var eacStep = gameClient.InstallationInstructions.PostInstallSteps.FirstOrDefault(s =>
            string.Equals(s.TargetRelativePath, GameClientConstants.GeneralsOnlineEacSetupExecutable, StringComparison.OrdinalIgnoreCase));
        Assert.Null(eacStep);
    }

    /// <summary>
    /// The factory declares the bootstrapper as the manifest entry point, so the
    /// launch pipeline reads data instead of depending on executable-flag fallbacks.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_EacLayout_DeclaresBootstrapperEntryPointAsync()
    {
        WriteEacPortableLayout();

        var gameClient = await CreateGameClientManifestAsync();

        Assert.Equal(
            GameClientConstants.GeneralsOnlineEacLauncherExecutable,
            Path.GetFileName(gameClient.EntryPoint),
            ignoreCase: true);
    }

    /// <summary>
    /// The factory declares the launch relationship from the package's own
    /// <c>EasyAntiCheat/Settings.json</c>: whatever binary the bootstrapper starts,
    /// including names GenHub has never seen, is what the launch adopts.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_SettingsNamesWrappedBinary_DeclaresRelationshipAsync()
    {
        WriteEacPortableLayout();
        WriteSettingsJson(GameClientConstants.GeneralsOnline60HzExecutable);

        var gameClient = await CreateGameClientManifestAsync();

        Assert.NotNull(gameClient.LaunchRelationship);
        Assert.Equal(
            Path.GetFileNameWithoutExtension(GameClientConstants.GeneralsOnline60HzExecutable),
            gameClient.LaunchRelationship!.ProcessName,
            ignoreCase: true);
        Assert.Equal(ProcessConstants.SpawnedChildDiscoveryTimeoutMs, gameClient.LaunchRelationship!.DiscoveryTimeoutMs);
    }

    /// <summary>
    /// When settings names a secondary test binary (the 092826 TestEnvironment layout)
    /// while the 60Hz live client is present, the 60Hz manifest targets the live client
    /// directly without EAC wrapping so players join live servers.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_SettingsNamesTestBinary_TargetsLiveSixtyHertzDirectlyAsync()
    {
        const string testBinary = "GeneralsOnlineZH_TestEnvironment.exe";
        WriteEacPortableLayout();
        WriteFile(testBinary);
        WriteSettingsJson(testBinary);

        var gameClient = await CreateGameClientManifestAsync();

        Assert.Equal(
            GameClientConstants.GeneralsOnline60HzExecutable,
            Path.GetFileName(gameClient.EntryPoint),
            ignoreCase: true);
        Assert.Null(gameClient.LaunchRelationship);
    }

    /// <summary>
    /// A renamed wrapped binary (when 60Hz binary is absent) is declared
    /// verbatim from settings so bootstrapper adoption works for future renames.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_SettingsNamesRenamedBinary_DeclaresRelationshipAsync()
    {
        const string renamedBinary = "GeneralsOnlineZH_Renamed.exe";
        WriteFile(GameClientConstants.GeneralsOnlineEacLauncherExecutable);
        WriteFile(GameClientConstants.GeneralsOnlineEacSetupExecutable);
        WriteFile(renamedBinary);
        WriteSettingsJson(renamedBinary);

        var gameClient = await CreateGameClientManifestAsync();

        Assert.NotNull(gameClient.LaunchRelationship);
        Assert.Equal("GeneralsOnlineZH_Renamed", gameClient.LaunchRelationship!.ProcessName);
        Assert.Equal(ProcessConstants.SpawnedChildDiscoveryTimeoutMs, gameClient.LaunchRelationship!.DiscoveryTimeoutMs);
    }

    /// <summary>
    /// Unreadable settings degrade to no declaration: the entry is still declared,
    /// and the launch falls back to legacy guessing for the child.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_UnreadableSettings_OmitsRelationshipAsync()
    {
        WriteEacPortableLayout();

        var gameClient = await CreateGameClientManifestAsync();

        Assert.NotNull(gameClient.EntryPoint);
        Assert.Null(gameClient.LaunchRelationship);
    }

    /// <summary>
    /// Settings naming a binary absent from the package degrade to no declaration
    /// rather than adopting a process that cannot exist.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_SettingsNameMissingBinary_OmitsRelationshipAsync()
    {
        WriteEacPortableLayout();
        WriteSettingsJson("GeneralsOnlineZH_Missing.exe");

        var gameClient = await CreateGameClientManifestAsync();

        Assert.Null(gameClient.LaunchRelationship);
    }

    /// <summary>
    /// Pre-EAC layouts declare the 60Hz binary as a direct entry with no relationship:
    /// the entry is the game itself.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_PreEacLayout_DeclaresDirectEntryWithoutRelationshipAsync()
    {
        WriteFile(GameClientConstants.GeneralsOnline60HzExecutable);
        WriteFile("libcurl.dll");

        var gameClient = await CreateGameClientManifestAsync();

        Assert.Equal(
            GameClientConstants.GeneralsOnline60HzExecutable,
            Path.GetFileName(gameClient.EntryPoint),
            ignoreCase: true);
        Assert.Null(gameClient.LaunchRelationship);
    }

    /// <summary>
    /// A local install carrying only the bootstrapper still imports: the wrapped
    /// binary needs no fixed file name because settings name it.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestsFromLocalInstallAsync_BootstrapperOnly_ImportsAsync()
    {
        WriteFile(GameClientConstants.GeneralsOnlineEacLauncherExecutable);
        WriteFile("GeneralsOnlineZH_TestEnvironment.exe");
        WriteSettingsJson("GeneralsOnlineZH_TestEnvironment.exe");

        var providerLoader = new Mock<IProviderDefinitionLoader>();
        var factory = new GeneralsOnlineManifestFactory(
            NullLogger<GeneralsOnlineManifestFactory>.Instance,
            providerLoader.Object);

        var manifests = await factory.CreateManifestsFromLocalInstallAsync(_extractedDirectory);

        var gameClient = Assert.Single(manifests, manifest => manifest.ContentType == ContentType.GameClient);
        Assert.NotNull(gameClient.LaunchRelationship);
        Assert.Equal("GeneralsOnlineZH_TestEnvironment", gameClient.LaunchRelationship!.ProcessName);
    }

    /// <summary>
    /// A directory with no supported entry point still imports nothing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestsFromLocalInstallAsync_NoEntryPoint_ImportsNothingAsync()
    {
        WriteFile("libcurl.dll");

        var providerLoader = new Mock<IProviderDefinitionLoader>();
        var factory = new GeneralsOnlineManifestFactory(
            NullLogger<GeneralsOnlineManifestFactory>.Instance,
            providerLoader.Object);

        var manifests = await factory.CreateManifestsFromLocalInstallAsync(_extractedDirectory);

        Assert.Empty(manifests);
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

    private void WriteSettingsJson(string executable)
    {
        var settingsPath = Path.Combine(_extractedDirectory, "EasyAntiCheat", "Settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, "{\"executable\": \"" + executable + "\"}");
    }

    private async Task<ContentManifest> CreateGameClientManifestAsync(ContentManifest? originalManifest = null)
    {
        var providerLoader = new Mock<IProviderDefinitionLoader>();
        var factory = new GeneralsOnlineManifestFactory(
            NullLogger<GeneralsOnlineManifestFactory>.Instance,
            providerLoader.Object);

        var result = await factory.CreateManifestsFromExtractedContentAsync(
            originalManifest ?? CreateOriginalManifest(),
            _extractedDirectory);

        Assert.True(result.Success);
        return result.Data!.Single(manifest => manifest.ContentType == ContentType.GameClient);
    }
}
