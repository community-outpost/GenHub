using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.Publishers;
using GenHub.Features.GameInstallations;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.GameInstallations;

/// <summary>
/// Detects an installation, then builds a fresh <see cref="GameInstallationService"/> over the
/// same manifest pool to simulate an app restart, and checks that the clients rebuilt from the
/// persisted manifests match first detection.
/// </summary>
public sealed class GameInstallationServiceRestartTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("GenHub.InstallRestart.").FullName;
    private readonly Dictionary<string, ContentManifest> _pool = new(StringComparer.OrdinalIgnoreCase);
    private int _clientDetectionCalls;

    /// <inheritdoc/>
    public void Dispose()
    {
        Directory.Delete(_root, true);
    }

    /// <summary>
    /// A flat native deployment ships only the Zero Hour engine. After restart it keeps the
    /// TheSuperHackers client and its native executable, and gains no Generals client.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task NativeFlatInstall_AfterRestart_ReloadsTheSuperHackersClientAsync()
    {
        File.WriteAllText(Path.Combine(_root, GameClientConstants.GeneralsIniBig), "archive");
        File.WriteAllText(Path.Combine(_root, GameClientConstants.ZeroHourIniBig), "archive");
        var enginePath = Path.Combine(_root, Path.GetFileNameWithoutExtension(GameClientConstants.SuperHackersZeroHourExecutable));
        File.WriteAllText(enginePath, "engine");

        GameClient[] DetectClients(GameInstallation installation) =>
        [
            new GameClient
            {
                Name = $"{SuperHackersConstants.PublisherName} - {SuperHackersConstants.ZeroHourDisplayName}",
                Id = string.Empty,
                Version = GameClientConstants.UnknownVersion,
                ExecutablePath = enginePath,
                GameType = GameType.ZeroHour,
                InstallationId = installation.Id,
                WorkingDirectory = _root,
                SourceType = ContentType.GameClient,
                PublisherType = PublisherTypeConstants.TheSuperHackers,
            },
        ];

        var firstRun = await LoadClientsAsync(() => CreateInstallation(_root, _root), DetectClients);
        var secondRun = await LoadClientsAsync(() => CreateInstallation(_root, _root), DetectClients);

        Assert.Equal(1, _clientDetectionCalls);
        var client = Assert.Single(secondRun);
        Assert.Equal(GameType.ZeroHour, client.GameType);
        Assert.Equal(enginePath, client.ExecutablePath);
        Assert.Equal(PublisherTypeConstants.TheSuperHackers, client.PublisherType);
        Assert.Equal(Snapshot(firstRun), Snapshot(secondRun));
    }

    /// <summary>
    /// A retail install with both games keeps each game's client name and executable after restart.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task RetailInstall_AfterRestart_ReloadsBothBaseClientsAsync()
    {
        var generalsPath = Directory.CreateDirectory(Path.Combine(_root, GameClientConstants.GeneralsDirectoryName)).FullName;
        var zeroHourPath = Directory.CreateDirectory(Path.Combine(_root, GameClientConstants.ZeroHourDirectoryName)).FullName;
        File.WriteAllText(Path.Combine(generalsPath, GameClientConstants.GeneralsIniBig), "archive");
        File.WriteAllText(Path.Combine(generalsPath, GameClientConstants.GeneralsExecutable), "exe");
        File.WriteAllText(Path.Combine(zeroHourPath, GameClientConstants.ZeroHourIniBig), "archive");
        File.WriteAllText(Path.Combine(zeroHourPath, GameClientConstants.ZeroHourExecutable), "exe");

        GameClient[] DetectClients(GameInstallation installation) =>
        [
            CreateBaseClient(installation, GameType.Generals, "Generals 1.08", "1.08", generalsPath),
            CreateBaseClient(installation, GameType.ZeroHour, "Zero Hour 1.04", "1.04", zeroHourPath),
        ];

        var firstRun = await LoadClientsAsync(() => CreateInstallation(generalsPath, zeroHourPath), DetectClients);
        var secondRun = await LoadClientsAsync(() => CreateInstallation(generalsPath, zeroHourPath), DetectClients);

        Assert.Equal(1, _clientDetectionCalls);
        Assert.Equal(2, secondRun.Count);
        Assert.Equal(Snapshot(firstRun), Snapshot(secondRun));
    }

    private static GameClient CreateBaseClient(GameInstallation installation, GameType gameType, string name, string version, string gamePath) => new()
    {
        Name = name,
        Id = string.Empty,
        Version = version,
        ExecutablePath = Path.Combine(gamePath, GameClientConstants.GeneralsExecutable),
        GameType = gameType,
        InstallationId = installation.Id,
        WorkingDirectory = gamePath,
    };

    private static List<(string Id, string Name, string ExecutablePath, string WorkingDirectory, GameType GameType)> Snapshot(IEnumerable<GameClient> clients) =>
        clients
            .Select(c => (c.Id, c.Name, c.ExecutablePath, c.WorkingDirectory, c.GameType))
            .OrderBy(c => c.GameType)
            .ToList();

    private static Mock<IContentManifestBuilder> CreateBuilder(Func<ContentManifest> build)
    {
        var builder = new Mock<IContentManifestBuilder>();
        builder.Setup(b => b.Build()).Returns(build);
        return builder;
    }

    private GameInstallation CreateInstallation(string generalsPath, string zeroHourPath)
    {
        var installation = new GameInstallation(_root, GameInstallationType.Retail, null);
        installation.SetPaths(generalsPath, zeroHourPath);
        return installation;
    }

    private async Task<IReadOnlyList<GameClient>> LoadClientsAsync(
        Func<GameInstallation> detectInstallation,
        Func<GameInstallation, GameClient[]> detectClients)
    {
        var installationDetection = new Mock<IGameInstallationDetectionOrchestrator>();
        installationDetection
            .Setup(d => d.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => DetectionResult<GameInstallation>.CreateSuccess([detectInstallation()], TimeSpan.Zero));

        var clientDetection = new Mock<IGameClientDetectionOrchestrator>();
        clientDetection
            .Setup(c => c.DetectGameClientsFromInstallationsAsync(It.IsAny<IEnumerable<IGameInstallation>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<IGameInstallation> installations, CancellationToken _) =>
            {
                _clientDetectionCalls++;
                var clients = installations.OfType<GameInstallation>().SelectMany(detectClients).ToList();
                return DetectionResult<GameClient>.CreateSuccess(clients, TimeSpan.Zero);
            });

        var manifestGeneration = new Mock<IManifestGenerationService>();
        manifestGeneration
            .Setup(m => m.CreateGameInstallationManifestAsync(
                It.IsAny<string>(),
                It.IsAny<GameType>(),
                It.IsAny<GameInstallationType>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, GameType gameType, GameInstallationType _, string? version, string? _, CancellationToken _) =>
                CreateBuilder(() => new ContentManifest
                {
                    Name = gameType.ToString().ToLowerInvariant(),
                    Version = version ?? string.Empty,
                    TargetGame = gameType,
                }).Object);
        manifestGeneration
            .Setup(m => m.CreateGameClientManifestAsync(
                It.IsAny<string>(),
                It.IsAny<GameType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<PublisherInfo?>()))
            .ReturnsAsync((string _, GameType gameType, string _, string version, string executablePath, PublisherInfo? _) =>
                CreateBuilder(() => new ContentManifest
                {
                    Name = gameType.ToString().ToLowerInvariant(),
                    Version = version,
                    TargetGame = gameType,
                    EntryPoint = Path.GetFileName(executablePath),
                    Files = [new ManifestFile { RelativePath = Path.GetFileName(executablePath), IsExecutable = true }],
                }).Object);

        var manifestPool = new Mock<IContentManifestPool>();
        manifestPool
            .Setup(p => p.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManifestId id, CancellationToken _) => _pool.TryGetValue(id.Value, out var manifest)
                ? OperationResult<ContentManifest?>.CreateSuccess(manifest)
                : OperationResult<ContentManifest?>.CreateFailure("Not found"));
        manifestPool
            .Setup(p => p.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<IProgress<ContentStorageProgress>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentManifest manifest, string _, IProgress<ContentStorageProgress>? _, CancellationToken _) =>
            {
                _pool[manifest.Id.Value] = manifest;
                return OperationResult<bool>.CreateSuccess(true);
            });
        manifestPool
            .Setup(p => p.SearchManifestsAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentSearchQuery query, CancellationToken _) => OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(
                _pool.Values
                    .Where(m => query.ContentType is null || m.ContentType == query.ContentType)
                    .Where(m => query.TargetGame is null || m.TargetGame == query.TargetGame)
                    .ToList()));

        using var service = new GameInstallationService(
            installationDetection.Object,
            clientDetection.Object,
            NullLogger<GameInstallationService>.Instance,
            manifestGeneration.Object,
            manifestPool.Object,
            gameClientIdentifiers: [new SuperHackersClientIdentifier()]);

        var result = await service.GetAllInstallationsAsync();

        Assert.True(result.Success, string.Join("; ", result.Errors));
        return Assert.Single(result.Data!).AvailableGameClients;
    }
}
