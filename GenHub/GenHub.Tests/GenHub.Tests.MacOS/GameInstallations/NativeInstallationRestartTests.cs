using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Infrastructure.DependencyInjection;
using GenHub.MacOS.Infrastructure.DependencyInjection;
using GenHub.Tests.MacOS.Infrastructure.DependencyInjection;
using GenHub.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace GenHub.Tests.MacOS.GameInstallations;

/// <summary>
/// Detects a flat native TheSuperHackers deployment with the real macOS container, then
/// restarts over the same data root and checks the reloaded clients match first detection.
/// </summary>
[SupportedOSPlatform("macos")]
[Collection(ApplicationCompositionCollection.Name)]
public class NativeInstallationRestartTests
{
    /// <summary>
    /// The reloaded install carries the same clients as first detection, and no Generals
    /// client appears for a deployment that ships only the Zero Hour engine.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task NativeFlatInstall_AfterRestart_ReloadsClientsFromFirstDetectionAsync()
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
        File.WriteAllBytes(enginePath, [0xCF, 0xFA, 0xED, 0xFE, 0x0C, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00]);

        var firstRun = await DetectClientsAsync(home, deployRoot);
        var secondRun = await DetectClientsAsync(home, deployRoot);

        var client = Assert.Single(firstRun);
        Assert.Equal(GameType.ZeroHour, client.GameType);
        Assert.Equal($"{SuperHackersConstants.PublisherName} - {SuperHackersConstants.ZeroHourDisplayName}", client.Name);
        Assert.Equal(PublisherTypeConstants.TheSuperHackers, client.PublisherType);
        Assert.Equal(enginePath, client.ExecutablePath);
        Assert.Equal(firstRun, secondRun);
    }

    private static async Task<List<ClientSnapshot>> DetectClientsAsync(string home, string deployRoot)
    {
        var services = new ServiceCollection();
        services.ConfigureApplicationServices(platformServices => platformServices.AddMacOSServices());
        using var serviceProvider = services.BuildServiceProvider();

        AssertUnderTemporaryDirectory(home);
        AssertUnderTemporaryDirectory(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        var pathProvider = serviceProvider.GetRequiredService<IGamePathProvider>();
        AssertUnderTemporaryDirectory(pathProvider.GetOptionsDirectory(GameType.Generals));
        AssertUnderTemporaryDirectory(pathProvider.GetOptionsDirectory(GameType.ZeroHour));

        var installationService = serviceProvider.GetRequiredService<IGameInstallationService>();
        var result = await installationService.GetAllInstallationsAsync();

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var installation = Assert.Single(result.Data!, i => string.Equals(i.InstallationPath, deployRoot, StringComparison.Ordinal));
        return installation.AvailableGameClients.Select(ClientSnapshot.From).ToList();
    }

    private static void AssertUnderTemporaryDirectory(string path)
    {
        string[] temporaryRoots = [Path.GetTempPath(), "/tmp/", "/private/tmp/", "/private/var/folders/"];
        Assert.Contains(temporaryRoots, root => path.StartsWith(root, StringComparison.Ordinal));
    }

    private sealed record ClientSnapshot(string Id, string Name, string ExecutablePath, string WorkingDirectory, GameType GameType, string? PublisherType)
    {
        public static ClientSnapshot From(GameClient client) =>
            new(client.Id, client.Name, client.ExecutablePath, client.WorkingDirectory, client.GameType, client.PublisherType);
    }
}
