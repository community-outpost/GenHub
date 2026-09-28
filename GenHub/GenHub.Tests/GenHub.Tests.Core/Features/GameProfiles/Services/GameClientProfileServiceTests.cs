using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.GameProfiles.Services;

/// <summary>
/// Unit tests for <see cref="GameClientProfileService"/>.
/// </summary>
public sealed class GameClientProfileServiceTests
{
    private const string InstallationPath = "/Games/ZeroHour";

    private readonly Mock<IGameProfileManager> _profileManagerMock = new();
    private readonly Mock<IGameInstallationService> _installationServiceMock = new();
    private readonly Mock<IConfigurationProviderService> _configServiceMock = new();
    private readonly Mock<IContentManifestPool> _manifestPoolMock = new();
    private readonly GameClientProfileService _service;
    private CreateProfileRequest? _capturedRequest;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameClientProfileServiceTests"/> class.
    /// </summary>
    public GameClientProfileServiceTests()
    {
        var installation = new GameInstallation(InstallationPath, GameInstallationType.Retail, null)
        {
            HasZeroHour = true,
            ZeroHourPath = InstallationPath,
        };

        _installationServiceMock
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([installation]));
        _profileManagerMock
            .Setup(m => m.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));
        _profileManagerMock
            .Setup(m => m.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .Callback((CreateProfileRequest request, CancellationToken _) => _capturedRequest = request)
            .ReturnsAsync((CreateProfileRequest request, CancellationToken _) =>
                ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "p1", Name = request.Name, GameClient = request.GameClient }));
        _manifestPoolMock
            .Setup(p => p.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateFailure("not pooled"));

        _service = new GameClientProfileService(
            _profileManagerMock.Object,
            _installationServiceMock.Object,
            _configServiceMock.Object,
            _manifestPoolMock.Object,
            NullLogger<GameClientProfileService>.Instance);
    }

    /// <summary>
    /// A manifest carrying both builds resolves to the host's form: the extensionless native
    /// binary on macOS and Linux, the <c>.exe</c> on Windows.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateProfileFromManifestAsync_WithWindowsAndNativeExecutables_UsesHostExecutableAsync()
    {
        var nativeName = Path.GetFileNameWithoutExtension(GameClientConstants.SuperHackersZeroHourExecutable);
        var manifest = CreateManifest(
            new ManifestFile { RelativePath = GameClientConstants.SuperHackersZeroHourExecutable },
            new ManifestFile { RelativePath = "libgamespy.dylib", IsExecutable = true },
            new ManifestFile { RelativePath = nativeName, IsExecutable = true });

        var result = await _service.CreateProfileFromManifestAsync(manifest);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var expected = OperatingSystem.IsWindows() ? GameClientConstants.SuperHackersZeroHourExecutable : nativeName;
        Assert.Equal(Path.Combine(InstallationPath, expected), _capturedRequest!.GameClient!.ExecutablePath);
    }

    /// <summary>
    /// A manifest for a native build has no <c>.exe</c> and still yields a profile.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateProfileFromManifestAsync_WithOnlyNativeExecutable_CreatesProfileAsync()
    {
        var nativeName = Path.GetFileNameWithoutExtension(GameClientConstants.SuperHackersZeroHourExecutable);
        var manifest = CreateManifest(
            new ManifestFile { RelativePath = "INIZH.big" },
            new ManifestFile { RelativePath = nativeName, IsExecutable = true });

        var result = await _service.CreateProfileFromManifestAsync(manifest);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(Path.Combine(InstallationPath, nativeName), _capturedRequest!.GameClient!.ExecutablePath);
    }

    /// <summary>
    /// A declared entry point wins over a helper executable listed before it.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateProfileFromManifestAsync_WithDeclaredEntryPoint_UsesItOverEarlierHelperAsync()
    {
        var nativeName = Path.GetFileNameWithoutExtension(GameClientConstants.SuperHackersZeroHourExecutable);
        var manifest = CreateManifest(
            new ManifestFile { RelativePath = "crashpad_handler", IsExecutable = true },
            new ManifestFile { RelativePath = nativeName, IsExecutable = true });
        manifest.EntryPoint = nativeName;

        var result = await _service.CreateProfileFromManifestAsync(manifest);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(Path.Combine(InstallationPath, nativeName), _capturedRequest!.GameClient!.ExecutablePath);
    }

    /// <summary>
    /// A declared entry point missing from the files fails with the resolver's reason instead of guessing.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateProfileFromManifestAsync_WithMissingDeclaredEntryPoint_FailsWithoutGuessingAsync()
    {
        var manifest = CreateManifest(
            new ManifestFile { RelativePath = "crashpad_handler", IsExecutable = true },
            new ManifestFile { RelativePath = GameClientConstants.SuperHackersZeroHourExecutable });
        manifest.EntryPoint = "generalszh";

        var result = await _service.CreateProfileFromManifestAsync(manifest);

        Assert.False(result.Success);
        Assert.Contains("generalszh", string.Join("; ", result.Errors));
        Assert.Null(_capturedRequest);
    }

    /// <summary>
    /// An existing profile is reported with a structured error code rather than only a message.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateProfileFromManifestAsync_WhenProfileExists_ReturnsAlreadyExistsErrorCodeAsync()
    {
        var manifest = CreateManifest(new ManifestFile { RelativePath = GameClientConstants.SuperHackersZeroHourExecutable });
        _profileManagerMock
            .Setup(m => m.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess(
                [new GameProfile { Id = "existing", Name = "Existing", GameClient = new GenHub.Core.Models.GameClients.GameClient { Id = manifest.Id.Value } }]));

        var result = await _service.CreateProfileFromManifestAsync(manifest);

        Assert.False(result.Success);
        Assert.Equal(ProfileConstants.ProfileAlreadyExistsErrorCode, result.ErrorCode);
    }

    private static ContentManifest CreateManifest(params ManifestFile[] files) => new()
    {
        Id = ManifestId.Create("1.20260925.thesuperhackers.gameclient.generalszh"),
        Name = "TheSuperHackers - Zero Hour",
        Version = "weekly-2026-09-25",
        ContentType = ContentType.GameClient,
        TargetGame = GameType.ZeroHour,
        Publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.TheSuperHackers },
        Files = [.. files],
    };
}
