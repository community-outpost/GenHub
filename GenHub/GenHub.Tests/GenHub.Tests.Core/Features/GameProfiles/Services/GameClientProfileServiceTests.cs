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
    private static readonly string InstallationPath = Path.GetFullPath("/Games/ZeroHour");

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
    /// Without a declared entry point, the known game executable in the host's form is chosen
    /// even when a helper executable is listed before it.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateProfileFromManifestAsync_WithoutEntryPoint_PicksGameExecutableOverEarlierHelperAsync()
    {
        var gameName = OperatingSystem.IsWindows()
            ? GameClientConstants.SuperHackersZeroHourExecutable
            : Path.GetFileNameWithoutExtension(GameClientConstants.SuperHackersZeroHourExecutable);
        var helperName = OperatingSystem.IsWindows() ? "crashpad_handler.exe" : "crashpad_handler";
        var manifest = CreateManifest(
            new ManifestFile { RelativePath = helperName, IsExecutable = true },
            new ManifestFile { RelativePath = gameName, IsExecutable = true });

        var result = await _service.CreateProfileFromManifestAsync(manifest);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(Path.Combine(InstallationPath, gameName), _capturedRequest!.GameClient!.ExecutablePath);
    }

    /// <summary>
    /// Without a declared entry point or a known game executable, several executables are
    /// ambiguous: profile creation fails instead of taking the first one.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateProfileFromManifestAsync_WithAmbiguousExecutables_FailsWithoutGuessingAsync()
    {
        var suffix = OperatingSystem.IsWindows() ? GameClientConstants.ExeExtension : string.Empty;
        var manifest = CreateManifest(
            new ManifestFile { RelativePath = "crashpad_handler" + suffix, IsExecutable = true },
            new ManifestFile { RelativePath = "custom_client" + suffix, IsExecutable = true });

        var result = await _service.CreateProfileFromManifestAsync(manifest);

        Assert.False(result.Success);
        Assert.Null(_capturedRequest);
    }

    /// <summary>
    /// Selection without a declared entry point, for each host. The host-form shortcut picks the
    /// <c>.exe</c> over its extensionless counterpart on Windows and the reverse elsewhere, but on
    /// Windows it steps aside when another known launch target such as <c>game.dat</c> or
    /// <c>generals.ctr</c> is present, so the resolver keeps its decision or reports ambiguity.
    /// </summary>
    /// <param name="files">Comma-separated file names in manifest order; a trailing <c>*</c> marks a file
    /// as needing execute permission, as native binaries are and Windows files are not.</param>
    /// <param name="isWindowsHost">Whether selection runs as on Windows.</param>
    /// <param name="expected">The expected file name, or <see langword="null"/> when selection must fail.</param>
    [Theory]
    [InlineData("generalszh.exe,generalszh*", true, "generalszh.exe")]
    [InlineData("generalszh*,generalszh.exe", true, "generalszh.exe")]
    [InlineData("generalszh.exe,generalszh*", false, "generalszh")]
    [InlineData("crashpad_handler*,generalszh*", false, "generalszh")]
    [InlineData("game.dat", true, "game.dat")]
    [InlineData("game.dat", false, "game.dat")]
    [InlineData("generals.ctr", true, "generals.ctr")]
    [InlineData("generals.ctr", false, "generals.ctr")]
    [InlineData("generals.exe,game.dat", true, null)]
    [InlineData("generals.exe,generals.ctr", true, null)]
    [InlineData("generals.exe,game.exe", true, null)]
    public void SelectClientExecutable_WithoutEntryPoint_HonoursHostAndCompetingTargets(string files, bool isWindowsHost, string? expected)
    {
        var manifest = CreateManifest(files.Split(',')
            .Select(name => new ManifestFile { RelativePath = name.TrimEnd('*'), IsExecutable = name.EndsWith('*') })
            .ToArray());

        var selected = GameClientProfileService.SelectClientExecutable(manifest, isWindowsHost, out var error);

        if (expected == null)
        {
            Assert.Null(selected);
            Assert.False(string.IsNullOrEmpty(error));
        }
        else
        {
            Assert.Equal(expected, selected?.RelativePath);
        }
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

    /// <summary>Unsafe declared and inferred entry points must not create profiles.</summary>
    /// <param name="prefix">The unsafe path prefix.</param>
    /// <param name="declared">Whether the entry point is declared.</param>
    /// <returns>The test task.</returns>
    [Theory]
    [InlineData("../", true)]
    [InlineData("../", false)]
    [InlineData("..\\", true)]
    [InlineData("..\\", false)]
    [InlineData("/outside/", true)]
    [InlineData("/outside/", false)]
    [InlineData("C:/outside/", true)]
    [InlineData("C:/outside/", false)]
    public async Task CreateProfileFromManifestAsync_UnsafeEntryPoint_RejectsProfileAsync(string prefix, bool declared)
    {
        var name = OperatingSystem.IsWindows() ? "generalszh.exe" : "generalszh";
        var path = prefix + name;
        var manifest = CreateManifest(new ManifestFile { RelativePath = path, IsExecutable = true });
        if (declared)
        {
            manifest.EntryPoint = path;
        }

        var result = await _service.CreateProfileFromManifestAsync(manifest);

        Assert.False(result.Success);
        Assert.Null(_capturedRequest);
        Assert.NotEmpty(result.Errors);
    }

    /// <summary>A Steam engine and launcher without an entry point must not silently select the stub.</summary>
    /// <returns>The test task.</returns>
    [Fact]
    public async Task CreateProfileFromManifestAsync_AmbiguousSteamExecutables_RejectsProfileAsync()
    {
        var manifest = CreateManifest(
            new ManifestFile { RelativePath = GameClientConstants.GeneralsExecutable, IsExecutable = true },
            new ManifestFile { RelativePath = GameClientConstants.SteamGameDatExecutable, IsExecutable = true });

        var result = await _service.CreateProfileFromManifestAsync(manifest);

        Assert.False(result.Success);
        Assert.Null(_capturedRequest);
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
