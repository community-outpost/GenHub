using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Infrastructure.DependencyInjection;
using GenHub.MacOS.Infrastructure.DependencyInjection;
using GenHub.Tests.MacOS.Infrastructure.DependencyInjection;
using GenHub.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Tests.MacOS.GameProfiles;

/// <summary>
/// Runs the scan-for-games profile creation through the real macOS container for a native
/// TheSuperHackers deployment, with only the wizard answer and the content orchestrator faked.
/// </summary>
[SupportedOSPlatform("macos")]
[Collection(ApplicationCompositionCollection.Name)]
public class NativePublisherProfileCreationTests
{
    private static readonly byte[] MachOHeader = [0xCF, 0xFA, 0xED, 0xFE, 0x0C, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00];

    /// <summary>
    /// A native install gets a profile on its own native executable, and neither wizard
    /// answer searches for or downloads the Windows TheSuperHackers package. A Windows build in
    /// the same folder does not take the action meant for the native one.
    /// </summary>
    /// <param name="wizardAction">The TheSuperHackers action the wizard returns.</param>
    /// <param name="withWindowsBuild">Whether a Windows <c>generalszh.exe</c> sits beside the native build.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(GameClientConstants.WizardActionTypes.CreateProfile, false)]
    [InlineData(GameClientConstants.WizardActionTypes.Install, false)]
    [InlineData(GameClientConstants.WizardActionTypes.CreateProfile, true)]
    public async Task ScanForGames_NativeSuperHackersInstall_CreatesNativeProfileWithoutDownloadAsync(string wizardAction, bool withWindowsBuild)
    {
        using var testEnvironment = new TemporaryApplicationEnvironment();
        var home = Environment.GetEnvironmentVariable("HOME")!;
        Directory.CreateDirectory(Path.Combine(home, "Documents"));
        Directory.CreateDirectory(Path.Combine(home, "Library", "Application Support"));
        var deployRoot = Path.Combine(
            home,
            GameClientConstants.NativeDeployParentDirectoryName,
            GameClientConstants.NativeDeployZeroHourDirectoryName);
        Directory.CreateDirectory(deployRoot);
        File.WriteAllText(Path.Combine(deployRoot, GameClientConstants.GeneralsIniBig), "archive");
        File.WriteAllText(Path.Combine(deployRoot, GameClientConstants.ZeroHourIniBig), "archive");
        var enginePath = Path.Combine(deployRoot, Path.GetFileNameWithoutExtension(GameClientConstants.SuperHackersZeroHourExecutable));
        File.WriteAllBytes(enginePath, MachOHeader);
        if (withWindowsBuild)
        {
            File.WriteAllBytes(Path.Combine(deployRoot, GameClientConstants.SuperHackersZeroHourExecutable), [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]);
        }

        var contentOrchestrator = new Mock<IContentOrchestrator>();
        contentOrchestrator
            .Setup(o => o.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess([WindowsPackageSearchResult()]));
        contentOrchestrator
            .Setup(o => o.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateFailure("download attempted"));

        var wizard = new Mock<ISetupWizardService>();
        wizard
            .Setup(w => w.RunSetupWizardAsync(It.IsAny<IEnumerable<GameInstallation>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SetupWizardResult
            {
                Confirmed = true,
                CommunityPatchAction = GameClientConstants.WizardActionTypes.Decline,
                CommunityPatchNonRetAction = GameClientConstants.WizardActionTypes.Decline,
                GeneralsOnlineAction = GameClientConstants.WizardActionTypes.Decline,
                SuperHackersAction = wizardAction,
            });

        var services = new ServiceCollection();
        services.ConfigureApplicationServices(platformServices => platformServices.AddMacOSServices());

        // SpecialFolder.MyDocuments can remain tied to the real account despite a temporary HOME.
        var isolatedPaths = new Mock<IGamePathProvider>();
        isolatedPaths.Setup(p => p.GetOptionsDirectory(It.IsAny<GameType>()))
            .Returns((GameType gameType) => Path.Combine(home, "Documents", gameType.ToString()));
        services.Replace(ServiceDescriptor.Singleton(isolatedPaths.Object));
        services.Replace(ServiceDescriptor.Singleton(contentOrchestrator.Object));
        services.Replace(ServiceDescriptor.Singleton(wizard.Object));
        using var serviceProvider = services.BuildServiceProvider();

        AssertUnderTemporaryDirectory(home);
        AssertUnderTemporaryDirectory(testEnvironment.AppDataPath);
        var pathProvider = serviceProvider.GetRequiredService<IGamePathProvider>();
        AssertUnderTemporaryDirectory(pathProvider.GetOptionsDirectory(GameType.ZeroHour));

        var launcher = serviceProvider.GetRequiredService<GameProfileLauncherViewModel>();
        await launcher.ScanForGamesCommand.ExecuteAsync(null);

        contentOrchestrator.Verify(
            o => o.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        contentOrchestrator.Verify(
            o => o.SearchAsync(It.Is<ContentSearchQuery>(q => q.ProviderName == PublisherTypeConstants.TheSuperHackers), It.IsAny<CancellationToken>()),
            Times.Never);

        var profiles = await serviceProvider.GetRequiredService<IGameProfileManager>().GetAllProfilesAsync();
        Assert.True(profiles.Success, string.Join("; ", profiles.Errors));
        var profile = Assert.Single(profiles.Data!);
        Assert.Equal(PublisherTypeConstants.TheSuperHackers, profile.GameClient!.PublisherType);
        Assert.Equal(enginePath, profile.GameClient.ExecutablePath);
    }

    private static ContentSearchResult WindowsPackageSearchResult() => new()
    {
        Id = "generalszh-weekly-2026-09-25.zip",
        Name = "TheSuperHackers weekly-2026-09-25",
        Version = "weekly-2026-09-25",
        ContentType = ContentType.GameClient,
        ProviderName = PublisherTypeConstants.TheSuperHackers,
    };

    private static void AssertUnderTemporaryDirectory(string path)
    {
        string[] temporaryRoots = [Path.GetTempPath(), "/tmp/", "/private/tmp/", "/private/var/folders/"];
        Assert.Contains(temporaryRoots, root => path.StartsWith(root, StringComparison.Ordinal));
    }
}
