using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Storage;
using GenHub.Features.Storage.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Storage;

/// <summary>
/// Tests installation CAS pool selection when the pool location cannot be written.
/// </summary>
public sealed class CasPoolWritabilityTests : IDisposable
{
    private readonly Mock<IUserSettingsService> _userSettingsService = new();
    private readonly Mock<IStorageWritabilityProbe> _writabilityProbe = new();
    private readonly string _tempPath;
    private readonly string _primaryPoolPath;
    private readonly string _installationPoolPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="CasPoolWritabilityTests"/> class.
    /// </summary>
    public CasPoolWritabilityTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _primaryPoolPath = Path.Combine(_tempPath, "primary-pool");
        _installationPoolPath = Path.Combine(_tempPath, "Game", DirectoryNames.GenHubCasPool);
        Directory.CreateDirectory(_primaryPoolPath);
    }

    /// <summary>
    /// Treats a configured but unwritable installation pool as unavailable.
    /// </summary>
    [Fact]
    public void IsInstallationPoolAvailable_WhenPoolIsNotWritable_ReturnsFalse()
    {
        _writabilityProbe.Setup(probe => probe.CanCreateStorageAt(_installationPoolPath)).Returns(false);
        var resolver = CreateResolver(_installationPoolPath);

        Assert.False(resolver.IsInstallationPoolAvailable());
    }

    /// <summary>
    /// Exposes an existing unwritable pool for read-only lookup before settings migration runs.
    /// </summary>
    [Fact]
    public void GetLegacyInstallationPoolRootPaths_WhenCurrentPoolIsUnwritable_ReturnsCurrentPath()
    {
        Directory.CreateDirectory(_installationPoolPath);
        _writabilityProbe.Setup(probe => probe.CanCreateStorageAt(_installationPoolPath)).Returns(false);
        var resolver = CreateResolver(_installationPoolPath);

        var result = resolver.GetLegacyInstallationPoolRootPaths();

        Assert.Equal([_installationPoolPath], result);
    }

    /// <summary>
    /// Keeps a writable installation pool selected.
    /// </summary>
    [Fact]
    public void IsInstallationPoolAvailable_WhenPoolIsWritable_ReturnsTrue()
    {
        _writabilityProbe.Setup(probe => probe.CanCreateStorageAt(_installationPoolPath)).Returns(true);
        var resolver = CreateResolver(_installationPoolPath);

        Assert.True(resolver.IsInstallationPoolAvailable());
    }

    /// <summary>
    /// Routes installation-pool content to the primary pool when the installation pool is unwritable.
    /// </summary>
    /// <param name="contentType">The content type normally routed to installation storage.</param>
    [Theory]
    [InlineData(ContentType.GameClient)]
    [InlineData(ContentType.GameInstallation)]
    [InlineData(ContentType.Addon)]
    [InlineData(ContentType.Patch)]
    [InlineData(ContentType.Map)]
    [InlineData(ContentType.Mod)]
    public void ResolvePool_WhenInstallationPoolIsNotWritable_UsesPrimaryPool(ContentType contentType)
    {
        _writabilityProbe.Setup(probe => probe.CanCreateStorageAt(_installationPoolPath)).Returns(false);
        var resolver = CreateResolver(_installationPoolPath);

        Assert.Equal(CasPoolType.Primary, resolver.ResolvePool(contentType));
        Assert.Equal(_primaryPoolPath, resolver.GetPoolRootPath(contentType));
    }

    /// <summary>
    /// Keeps routing installation-pool content to a writable installation pool.
    /// </summary>
    [Fact]
    public void ResolvePool_WhenInstallationPoolIsWritable_UsesInstallationPool()
    {
        _writabilityProbe.Setup(probe => probe.CanCreateStorageAt(_installationPoolPath)).Returns(true);
        var resolver = CreateResolver(_installationPoolPath);

        Assert.Equal(CasPoolType.Installation, resolver.ResolvePool(ContentType.GameClient));
        Assert.Equal(_installationPoolPath, resolver.GetPoolRootPath(ContentType.GameClient));
    }

    /// <summary>
    /// Treats an empty installation pool path as unavailable without probing.
    /// </summary>
    [Fact]
    public void IsInstallationPoolAvailable_WhenPathIsEmpty_ReturnsFalseWithoutProbing()
    {
        var resolver = CreateResolver(string.Empty);

        Assert.False(resolver.IsInstallationPoolAvailable());
        _writabilityProbe.Verify(probe => probe.CanCreateStorageAt(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// Probes a real unwritable directory end to end rather than a mocked verdict.
    /// </summary>
    [Fact]
    public void StorageWritabilityProbe_WhenDirectoryDeniesWrites_ReturnsFalse()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var lockedPath = Path.Combine(_tempPath, "locked");
        Directory.CreateDirectory(lockedPath);
        File.SetUnixFileMode(lockedPath, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            var probe = new StorageWritabilityProbe(new Mock<ILogger<StorageWritabilityProbe>>().Object);

            Assert.False(probe.CanCreateStorageAt(Path.Combine(lockedPath, DirectoryNames.GenHubCasPool)));
            Assert.True(probe.CanCreateStorageAt(Path.Combine(_primaryPoolPath, DirectoryNames.GenHubCasPool)));
        }
        finally
        {
            File.SetUnixFileMode(
                lockedPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>
    /// Leaves no probe files behind after a successful check.
    /// </summary>
    [Fact]
    public void StorageWritabilityProbe_WhenLocationIsWritable_LeavesNoProbeFile()
    {
        var probe = new StorageWritabilityProbe(new Mock<ILogger<StorageWritabilityProbe>>().Object);
        var targetPath = Path.Combine(_primaryPoolPath, DirectoryNames.GenHubCasPool);

        Assert.True(probe.CanCreateStorageAt(targetPath));
        Assert.True(Directory.Exists(targetPath));
        Assert.Empty(Directory.GetFiles(targetPath, StorageConstants.WriteProbeFilePrefix + "*"));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempPath, true);
        }
        catch (IOException)
        {
            // Best-effort cleanup for temporary test files.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup for temporary test files.
        }

        GC.SuppressFinalize(this);
    }

    private CasPoolResolver CreateResolver(string installationPoolRootPath)
    {
        _userSettingsService
            .Setup(service => service.Get())
            .Returns(new UserSettings
            {
                CasConfiguration = new CasConfiguration
                {
                    CasRootPath = _primaryPoolPath,
                    InstallationPoolRootPath = installationPoolRootPath,
                },
            });

        return new CasPoolResolver(
            Options.Create(new CasConfiguration { CasRootPath = _primaryPoolPath }),
            _userSettingsService.Object,
            _writabilityProbe.Object,
            new Mock<ILogger<CasPoolResolver>>().Object);
    }
}
