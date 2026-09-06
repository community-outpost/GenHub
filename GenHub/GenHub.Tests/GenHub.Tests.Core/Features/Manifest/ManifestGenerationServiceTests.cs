using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Manifest;
using GenHub.Features.Workspace;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit.Abstractions;
using ContentType = GenHub.Core.Models.Enums.ContentType;
using GameInstallationType = GenHub.Core.Models.Enums.GameInstallationType;
using GameType = GenHub.Core.Models.Enums.GameType;

namespace GenHub.Tests.Core.Features.Manifest;

/// <summary>
/// Unit tests for <see cref="ManifestGenerationService"/> executable inclusion.
/// </summary>
public class ManifestGenerationServiceTests : IDisposable
{
    private readonly Mock<IFileHashProvider> _hashProviderMock;
    private readonly Mock<IManifestIdService> _manifestIdServiceMock;
    private readonly Mock<IDownloadService> _downloadServiceMock;
    private readonly Mock<IConfigurationProviderService> _configProviderServiceMock;
    private readonly ITestOutputHelper? _testOutput;
    private readonly ManifestGenerationService _service;
    private readonly string _tempDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManifestGenerationServiceTests"/> class.
    /// </summary>
    /// <param name="testOutput">Optional test output helper for test reporting.</param>
    public ManifestGenerationServiceTests(ITestOutputHelper? testOutput = null)
    {
        _testOutput = testOutput;
        _hashProviderMock = new Mock<IFileHashProvider>();
        _manifestIdServiceMock = new Mock<IManifestIdService>();
        _downloadServiceMock = new Mock<IDownloadService>();
        _configProviderServiceMock = new Mock<IConfigurationProviderService>();

        // Setup hash provider to return deterministic hashes
        _hashProviderMock.Setup(x => x.ComputeFileHashAsync(It.IsAny<string>(), default))
            .ReturnsAsync((string path, System.Threading.CancellationToken ct) => $"hash_{Path.GetFileName(path)}");

        // Setup manifest ID service to return properly formatted IDs
        // Format: version.userversion.publisher.contenttype.contentname
        // Publisher names need to be normalized (lowercase, no spaces)
        _manifestIdServiceMock.Setup(x => x.GenerateGameInstallationId(
                It.IsAny<GameInstallation>(),
                It.IsAny<GameType>(),
                It.IsAny<string?>()))
            .Returns((GameInstallation inst, GameType gt, string? v) => OperationResult<ManifestId>.CreateSuccess(ManifestId.Create("1.0.ea.gameinstallation.generals")));

        _manifestIdServiceMock.Setup(x => x.GeneratePublisherContentId(
                It.IsAny<string>(), It.IsAny<ContentType>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns((string p, ContentType ct, string c, int v) =>
            {
                var normalizedPublisher = p.ToLowerInvariant().Replace(" ", string.Empty);
                var normalizedContent = c.ToLowerInvariant().Replace(" ", string.Empty);
                var contentTypeString = ct.ToString().ToLowerInvariant();
                return OperationResult<ManifestId>.CreateSuccess(
                    ManifestId.Create($"1.{v}.{normalizedPublisher}.{contentTypeString}.{normalizedContent}"));
            });

        _service = new ManifestGenerationService(
            NullLogger<ManifestGenerationService>.Instance,
            _hashProviderMock.Object,
            _manifestIdServiceMock.Object,
            _downloadServiceMock.Object,
            _configProviderServiceMock.Object);

        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <summary>
    /// Tests that CreateGameClientManifestAsync includes the executable in the manifest.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateGameClientManifestAsync_IncludesExecutableWithHashAsync()
    {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "generals.exe"), "dummy exe content");
        var (clientPath, executablePath) = await PrepareDummyExeAsync();

        // Act
        var builder = await _service.CreateGameClientManifestAsync(
            clientPath, GameType.Generals, "TestClient", "1.0", executablePath);
        var manifest = builder.Build();

        // Assert
        Assert.NotNull(manifest);
        var executableFile = manifest.Files.FirstOrDefault(f => f.RelativePath.EndsWith("generals.exe"));
        Assert.NotNull(executableFile);
        Assert.Equal("hash_generals.exe", executableFile.Hash);
        Assert.Equal(GenHub.Core.Models.Enums.ContentSourceType.GameInstallation, executableFile.SourceType);
    }

    /// <summary>
    /// Tests that CreateGameClientManifestAsync includes executable with correct size.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateGameClientManifestAsync_IncludesExecutableWithCorrectSizeAsync()
    {
        // Arrange
        var (clientPath, executablePath) = await PrepareDummyExeAsync();
        var testContent = "This is test content for size calculation";
        await File.WriteAllTextAsync(executablePath, testContent);
        var expectedSize = new FileInfo(executablePath).Length;

        // Act
        var builder = await _service.CreateGameClientManifestAsync(
            clientPath, GameType.ZeroHour, "TestClient", "1.0", executablePath);
        var manifest = builder.Build();

        // Assert
        var executableFile = manifest.Files.FirstOrDefault(f => f.RelativePath.EndsWith("generals.exe"));
        Assert.NotNull(executableFile);
        Assert.Equal(expectedSize, executableFile.Size);
    }

    /// <summary>
    /// Tests that CreateGameClientManifestAsync throws when executable is missing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateGameClientManifestAsync_ThrowsWhenExecutableMissingAsync()
    {
        // Arrange
        var clientPath = Path.Combine(_tempDirectory, "TestClient");
        Directory.CreateDirectory(clientPath);
        var executablePath = Path.Combine(clientPath, "nonexistent.exe");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _service.CreateGameClientManifestAsync(
                clientPath, GameType.Generals, "TestClient", "1.0", executablePath));
    }

    /// <summary>
    /// Tests that CreateGameClientManifestAsync includes required DLLs when present.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateGameClientManifestAsync_IncludesRequiredDllsWhenPresentAsync()
    {
        // Arrange
        var (clientPath, executablePath) = await PrepareDummyExeAsync();

        // Create required DLLs
        await File.WriteAllTextAsync(Path.Combine(clientPath, "steam_api.dll"), "dll content");
        await File.WriteAllTextAsync(Path.Combine(clientPath, "binkw32.dll"), "dll content");

        // Act
        var builder = await _service.CreateGameClientManifestAsync(
            clientPath, GameType.Generals, "TestClient", "1.0", executablePath);
        var manifest = builder.Build();

        // Assert
        Assert.Contains(manifest.Files, f => f.RelativePath.EndsWith("steam_api.dll"));
        Assert.Contains(manifest.Files, f => f.RelativePath.EndsWith("binkw32.dll"));
    }

    /// <summary>
    /// Tests that CreateGameClientManifestAsync includes config files when present.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateGameClientManifestAsync_IncludesConfigFilesWhenPresentAsync()
    {
        // Arrange
        var (clientPath, executablePath) = await PrepareDummyExeAsync();

        // Create config files
        await File.WriteAllTextAsync(Path.Combine(clientPath, "options.ini"), "config content");
        await File.WriteAllTextAsync(Path.Combine(clientPath, "skirmish.ini"), "config content");

        // Act
        var builder = await _service.CreateGameClientManifestAsync(
            clientPath, GameType.Generals, "TestClient", "1.0", executablePath);
        var manifest = builder.Build();

        // Assert
        Assert.Contains(manifest.Files, f => f.RelativePath.EndsWith("options.ini"));
        Assert.Contains(manifest.Files, f => f.RelativePath.EndsWith("skirmish.ini"));
    }

    /// <summary>
    /// Tests that manifest Files section contains multiple items.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateGameClientManifestAsync_ManifestContainsMultipleFilesAsync()
    {
        // Arrange
        var (clientPath, executablePath) = await PrepareDummyExeAsync();

        // Create DLLs and config files
        await File.WriteAllTextAsync(Path.Combine(clientPath, "steam_api.dll"), "dll");
        await File.WriteAllTextAsync(Path.Combine(clientPath, "binkw32.dll"), "dll");
        await File.WriteAllTextAsync(Path.Combine(clientPath, "options.ini"), "config");

        // Act
        var builder = await _service.CreateGameClientManifestAsync(
            clientPath, GameType.Generals, "TestClient", "1.0", executablePath);
        var manifest = builder.Build();

        // Assert - At minimum: exe + 2 DLLs + 1 config = 4 files
        Assert.True(manifest.Files.Count >= 4, $"Expected at least 4 files, got {manifest.Files.Count}");
    }

    /// <summary>
    /// Tests that CreateGameClientManifestAsync includes all DLLs and Generals.dat for EA App clients.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateGameClientManifestAsync_IncludesAllDllsAndGeneralsDatForEaAppAsync()
    {
        // Arrange
        var clientPath = Path.Combine(_tempDirectory, "EaAppClient");
        Directory.CreateDirectory(clientPath);
        var executablePath = Path.Combine(clientPath, "game.dat");
        await File.WriteAllTextAsync(executablePath, "dummy game.dat");

        // Create various DLLs, some in RequiredDlls, some auxiliary
        await File.WriteAllTextAsync(Path.Combine(clientPath, "binkw32.dll"), "dll");
        await File.WriteAllTextAsync(Path.Combine(clientPath, "P2XDLL.DLL"), "ea wrapper");
        await File.WriteAllTextAsync(Path.Combine(clientPath, "patchw32.dll"), "patch dll");
        await File.WriteAllTextAsync(Path.Combine(clientPath, "custom_wrapper.dll"), "custom dll");

        // Create Generals.dat
        await File.WriteAllTextAsync(Path.Combine(clientPath, "Generals.dat"), "data file");

        // Act
        // Use "ea" in the client name to trigger EA App logic
        var builder = await _service.CreateGameClientManifestAsync(
            clientPath, GameType.ZeroHour, "EA App Zero Hour", "1.04", executablePath);
        var manifest = builder.Build();

        // Assert
        Assert.Contains(manifest.Files, f => f.RelativePath == "game.dat" && f.IsExecutable);
        Assert.Contains(manifest.Files, f => f.RelativePath == "binkw32.dll");
        Assert.Contains(manifest.Files, f => f.RelativePath == "P2XDLL.DLL");
        Assert.Contains(manifest.Files, f => f.RelativePath == "patchw32.dll");
        Assert.Contains(manifest.Files, f => f.RelativePath == "custom_wrapper.dll");
        Assert.Contains(manifest.Files, f => f.RelativePath == "Generals.dat");

        // Also verify required DLLs from GameClientConstants are included
        Assert.Contains(manifest.Files, f => f.RelativePath == "binkw32.dll");
    }

    /// <summary>
    /// Tests that CreateGameInstallationManifestAsync uses CSV-based generation.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateGameInstallationManifestAsync_UsesCsvWhenAvailableAsync()
    {
        // Arrange
        var installationPath = Path.Combine(_tempDirectory, "GeneralsInstall");
        Directory.CreateDirectory(installationPath);

        // Create some files that are in the Generals registry
        await File.WriteAllTextAsync(Path.Combine(installationPath, "generals.exe"), "dummy");
        await File.WriteAllTextAsync(Path.Combine(installationPath, "AudioEnglish.big"), "dummy");

        // Act
        var builder = await _service.CreateGameInstallationManifestAsync(
            installationPath, GameType.Generals, GameInstallationType.Steam, "1.08");
        var manifest = builder.Build();

        // Assert
        Assert.NotNull(manifest);
        Assert.Contains(manifest.Files, f => f.RelativePath == "generals.exe");
        Assert.Contains(manifest.Files, f => f.RelativePath == "AudioEnglish.big");
    }

    /// <summary>
    /// Tests that CreateGameInstallationManifestAsync generates authoritative manifest for Generals with SHA256 hashes and proper source type.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateGameInstallationManifestAsync_Generals_PopulatesAuthoritativeMetadataAsync()
    {
        // Arrange
        var installationPath = Path.Combine(_tempDirectory, "GeneralsAuthoritative");
        Directory.CreateDirectory(installationPath);

        await File.WriteAllTextAsync(Path.Combine(installationPath, "generals.exe"), "executable binary");
        await File.WriteAllTextAsync(Path.Combine(installationPath, "binkw32.dll"), "bink dll");
        await File.WriteAllTextAsync(Path.Combine(installationPath, "Audio.big"), "audio archive");

        // Act
        var builder = await _service.CreateGameInstallationManifestAsync(
            installationPath, GameType.Generals, GameInstallationType.Steam, "1.08", "EN");
        var manifest = builder.Build();

        // Assert
        Assert.NotNull(manifest);
        var exeFile = manifest.Files.FirstOrDefault(f => f.RelativePath == "generals.exe");
        Assert.NotNull(exeFile);
        Assert.True(exeFile.IsExecutable);
        Assert.Equal(GenHub.Core.Models.Enums.ContentSourceType.GameInstallation, exeFile.SourceType);
        Assert.Equal("e253361f457f2ec3290ccf4088aa5c4022fc4772a769fff5fb2fa8b9e5df842d", exeFile.Hash);

        var dllFile = manifest.Files.FirstOrDefault(f => f.RelativePath.Equals("binkw32.dll", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(dllFile);
        Assert.False(dllFile.IsExecutable);
        Assert.Equal("892a51c4056efcb22297a3b44a3491e3f5888f28b08ed1b17030f24acffedb44", dllFile.Hash);
    }

    /// <summary>
    /// Tests that CreateGameInstallationManifestAsync generates authoritative manifest for Zero Hour.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateGameInstallationManifestAsync_ZeroHour_ResolvesAuthoritativeFilesAsync()
    {
        // Arrange
        var installationPath = Path.Combine(_tempDirectory, "ZeroHourInstall");
        Directory.CreateDirectory(installationPath);

        await File.WriteAllTextAsync(Path.Combine(installationPath, "generals.exe"), "zh exe");
        await File.WriteAllTextAsync(Path.Combine(installationPath, "AudioZH.big"), "zh audio");
        await File.WriteAllTextAsync(Path.Combine(installationPath, "SpeechZH.big"), "zh speech");

        // Act
        var builder = await _service.CreateGameInstallationManifestAsync(
            installationPath, GameType.ZeroHour, GameInstallationType.EaApp, "1.04", "EN");
        var manifest = builder.Build();

        // Assert
        Assert.NotNull(manifest);
        Assert.Contains(manifest.Files, f => f.RelativePath == "generals.exe");
        Assert.Contains(manifest.Files, f => f.RelativePath == "AudioZH.big");
        Assert.Contains(manifest.Files, f => f.RelativePath == "SpeechZH.big");
    }

    /// <summary>
    /// Tests that CreateGameInstallationManifestAsync generates manifest for Zero Hour 1.05 via fallback directory scan.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateGameInstallationManifestAsync_ZeroHour105_FallbackScan_ResolvesFilesAsync()
    {
        // Arrange
        var installationPath = Path.Combine(_tempDirectory, "ZeroHour105Install");
        Directory.CreateDirectory(installationPath);

        await File.WriteAllTextAsync(Path.Combine(installationPath, "generals.exe"), "zh exe");
        await File.WriteAllTextAsync(Path.Combine(installationPath, "AudioZH.big"), "zh audio");

        // Act
        var builder = await _service.CreateGameInstallationManifestAsync(
            installationPath, GameType.ZeroHour, GameInstallationType.Steam, "1.05", "EN");
        var manifest = builder.Build();

        // Assert
        Assert.NotNull(manifest);
        Assert.Contains(manifest.Files, f => f.RelativePath == "generals.exe");
        Assert.Contains(manifest.Files, f => f.RelativePath == "AudioZH.big");
    }

    /// <summary>
    /// Tests that CreateGameInstallationManifestAsync filters language files according to requested language.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateGameInstallationManifestAsync_LanguageFiltering_IncludesMatchingLanguageOnlyAsync()
    {
        // Arrange
        var installationPath = Path.Combine(_tempDirectory, "LanguageGenerals");
        Directory.CreateDirectory(installationPath);

        await File.WriteAllTextAsync(Path.Combine(installationPath, "generals.exe"), "exe");
        await File.WriteAllTextAsync(Path.Combine(installationPath, "AudioEnglish.big"), "english audio");
        await File.WriteAllTextAsync(Path.Combine(installationPath, "English.big"), "english text");

        // Act 1: Request English (EN)
        var enBuilder = await _service.CreateGameInstallationManifestAsync(
            installationPath, GameType.Generals, GameInstallationType.Steam, "1.08", "EN");
        var enManifest = enBuilder.Build();

        // Assert 1: English files are included
        Assert.NotNull(enManifest);
        Assert.Contains(enManifest.Files, f => f.RelativePath == "AudioEnglish.big");
        Assert.Contains(enManifest.Files, f => f.RelativePath == "English.big");

        // Act 2: Request German (DE) for the same folder
        var deBuilder = await _service.CreateGameInstallationManifestAsync(
            installationPath, GameType.Generals, GameInstallationType.Steam, "1.08", "DE");
        var deManifest = deBuilder.Build();

        // Assert 2: English-specific files are excluded when German is requested
        Assert.NotNull(deManifest);
        Assert.DoesNotContain(deManifest.Files, f => f.RelativePath == "AudioEnglish.big");
        Assert.DoesNotContain(deManifest.Files, f => f.RelativePath == "English.big");
    }

    /// <summary>
    /// Tests that CreateGameInstallationManifestAsync excludes extra non-vanilla files from the core manifest.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateGameInstallationManifestAsync_ExcludesExtraNonVanillaFilesAsync()
    {
        // Arrange
        var installationPath = Path.Combine(_tempDirectory, "ModdedGenerals");
        Directory.CreateDirectory(installationPath);

        // Vanilla files
        await File.WriteAllTextAsync(Path.Combine(installationPath, "generals.exe"), "exe");
        await File.WriteAllTextAsync(Path.Combine(installationPath, "AudioEnglish.big"), "english audio");

        // Extra non-vanilla mod files
        await File.WriteAllTextAsync(Path.Combine(installationPath, "!Gentool.dll"), "gentool mod dll");
        await File.WriteAllTextAsync(Path.Combine(installationPath, "CustomShockwave.big"), "mod archive");
        await File.WriteAllTextAsync(Path.Combine(installationPath, "test_custom_config.ini"), "custom ini");

        // Act
        var builder = await _service.CreateGameInstallationManifestAsync(
            installationPath, GameType.Generals, GameInstallationType.Steam, "1.08", "EN");
        var manifest = builder.Build();

        // Assert
        Assert.NotNull(manifest);
        Assert.Contains(manifest.Files, f => f.RelativePath == "generals.exe");
        Assert.Contains(manifest.Files, f => f.RelativePath == "AudioEnglish.big");

        // Untracked/mod files should NOT be in the pristine vanilla manifest
        Assert.DoesNotContain(manifest.Files, f => f.RelativePath == "!Gentool.dll");
        Assert.DoesNotContain(manifest.Files, f => f.RelativePath == "CustomShockwave.big");
        Assert.DoesNotContain(manifest.Files, f => f.RelativePath == "test_custom_config.ini");
    }

    /// <summary>
    /// Tests that CreateGameInstallationManifestAsync uses backup (.bak) file if available.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateGameInstallationManifestAsync_UsesBackupFileWhenPresentAsync()
    {
        // Arrange
        var installationPath = Path.Combine(_tempDirectory, "BackupTest");
        Directory.CreateDirectory(installationPath);

        var originalExe = Path.Combine(installationPath, "generals.exe");
        var backupExe = Path.Combine(installationPath, "generals.exe.bak");
        await File.WriteAllTextAsync(originalExe, "modified exe");
        await File.WriteAllTextAsync(backupExe, "pristine original backup exe");

        // Act
        var builder = await _service.CreateGameInstallationManifestAsync(
            installationPath, GameType.Generals, GameInstallationType.Steam, "1.08", "EN");
        var manifest = builder.Build();

        // Assert
        Assert.NotNull(manifest);
        var exeFile = manifest.Files.FirstOrDefault(f => f.RelativePath == "generals.exe");
        Assert.NotNull(exeFile);
        Assert.Equal(backupExe, exeFile.SourcePath);
    }

    /// <summary>
    /// Tests that CreateGameInstallationManifestAsync falls back to directory scan when no authoritative CSV catalog is found,
    /// recursing into subdirectories and including broadened game file extensions while skipping backups.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateGameInstallationManifestAsync_UnsupportedVersion_FallsBackToDirectoryScanAsync()
    {
        // Arrange
        var installationPath = Path.Combine(_tempDirectory, "UnsupportedVersionInstall");
        Directory.CreateDirectory(installationPath);

        var exePath = Path.Combine(installationPath, "generals.exe");
        await File.WriteAllTextAsync(exePath, "legacy exe");
        var iniPath = Path.Combine(installationPath, "game.ini");
        await File.WriteAllTextAsync(iniPath, "config ini");

        // Subdirectory files matching broadened catalog extensions
        var dataCursorsDir = Path.Combine(installationPath, "Data", "Cursors");
        Directory.CreateDirectory(dataCursorsDir);
        await File.WriteAllTextAsync(Path.Combine(dataCursorsDir, "cursor.ani"), "cursor data");

        var mssDir = Path.Combine(installationPath, "MSS");
        Directory.CreateDirectory(mssDir);
        await File.WriteAllTextAsync(Path.Combine(mssDir, "mssa3d.m3d"), "sound driver");

        // Broadened game file extension in root
        await File.WriteAllTextAsync(Path.Combine(installationPath, "intro.bik"), "movie");

        // Files that should be skipped during scan
        var backupDir = Path.Combine(installationPath, ".genhub-backup");
        Directory.CreateDirectory(backupDir);
        await File.WriteAllTextAsync(Path.Combine(backupDir, "backup.exe"), "backup exe");
        await File.WriteAllTextAsync(Path.Combine(installationPath, "game.ini.bak"), "backup ini");
        await File.WriteAllTextAsync(Path.Combine(installationPath, "debug.log"), "log file");
        var gitDir = Path.Combine(installationPath, FileTypes.GitDirectoryName);
        Directory.CreateDirectory(gitDir);
        await File.WriteAllTextAsync(Path.Combine(gitDir, "config"), "git config");

        // Act - version "1.02" has no authoritative CSV catalog
        var builder = await _service.CreateGameInstallationManifestAsync(
            installationPath, GameType.Generals, GameInstallationType.Retail, "1.02");
        var manifest = builder.Build();

        // Assert - Should successfully fall back to directory scan, capturing nested files and ignoring skipped ones
        Assert.NotNull(manifest);
        Assert.Contains(manifest.Files, f => f.RelativePath == "generals.exe");
        Assert.Contains(manifest.Files, f => f.RelativePath == "game.ini");
        Assert.Contains(manifest.Files, f => f.RelativePath == "Data/Cursors/cursor.ani");
        Assert.Contains(manifest.Files, f => f.RelativePath == "MSS/mssa3d.m3d");
        Assert.Contains(manifest.Files, f => f.RelativePath == "intro.bik");
        Assert.DoesNotContain(manifest.Files, f => f.RelativePath.Contains("backup"));
        Assert.DoesNotContain(manifest.Files, f => f.RelativePath.StartsWith(FileTypes.GitDirectoryName + "/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(manifest.Files, f => f.RelativePath.EndsWith(FileTypes.LegacyBackupExtension, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(manifest.Files, f => f.RelativePath.EndsWith(".log", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Tests that an IOException on a single file during fallback scan does not abort remaining files.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateGameInstallationManifestAsync_FallbackScan_SingleFileLocked_ContinuesRemainingFilesAsync()
    {
        // Arrange
        var installationPath = Path.Combine(_tempDirectory, "LockedFallbackInstall");
        Directory.CreateDirectory(installationPath);

        var exePath = Path.Combine(installationPath, "generals.exe");
        await File.WriteAllTextAsync(exePath, "legacy exe");

        var lockedFile = Path.Combine(installationPath, "locked.ini");
        await File.WriteAllTextAsync(lockedFile, "locked content");

        var goodFile = Path.Combine(installationPath, "good.ini");
        await File.WriteAllTextAsync(goodFile, "good content");

        _hashProviderMock
            .Setup(x => x.ComputeFileHashAsync(It.Is<string>(p => p.EndsWith("locked.ini", StringComparison.OrdinalIgnoreCase)), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("File is locked by another process"));

        // Act - version "1.02" has no authoritative CSV catalog
        var builder = await _service.CreateGameInstallationManifestAsync(
            installationPath, GameType.Generals, GameInstallationType.Retail, "1.02");
        var manifest = builder.Build();

        // Assert - locked file skipped, but other files still added
        Assert.NotNull(manifest);
        Assert.Contains(manifest.Files, f => f.RelativePath == "generals.exe");
        Assert.Contains(manifest.Files, f => f.RelativePath == "good.ini");
        Assert.DoesNotContain(manifest.Files, f => f.RelativePath == "locked.ini");
    }

    /// <summary>
    /// Tests that CreateGameInstallationManifestAsync skips symbolic links and reparse points during fallback directory scan,
    /// including symlinked files, directories, and primary executables.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateGameInstallationManifestAsync_FallbackScan_SkipsReparsePointsAndSymbolicLinksAsync()
    {
        // Arrange
        var installationPath = Path.Combine(_tempDirectory, "ReparseFallbackInstall");
        Directory.CreateDirectory(installationPath);

        var normalFile = Path.Combine(installationPath, "normal.ini");
        await File.WriteAllTextAsync(normalFile, "normal content");

        // External directory and files outside the installation
        var outsideDir = Path.Combine(_tempDirectory, "OutsideTargetFolder");
        Directory.CreateDirectory(outsideDir);
        var outsideFile = Path.Combine(outsideDir, "outside.big");
        await File.WriteAllTextAsync(outsideFile, "outside content");
        var outsideExe = Path.Combine(outsideDir, "outside_generals.exe");
        await File.WriteAllTextAsync(outsideExe, "outside exe");

        // Create symlinked file, symlinked directory, and symlinked primary executable under installation
        var symlinkFile = Path.Combine(installationPath, "linked_file.big");
        var symlinkDir = Path.Combine(installationPath, "linked_dir");
        var symlinkExe = Path.Combine(installationPath, "generals.exe");

        try
        {
            File.CreateSymbolicLink(symlinkFile, outsideFile);
            Directory.CreateSymbolicLink(symlinkDir, outsideDir);
            File.CreateSymbolicLink(symlinkExe, outsideExe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            _testOutput?.WriteLine($"Skipping symlink test: symbolic link creation requires elevated privileges or Developer Mode on this platform ({ex.Message}).");
            return;
        }

        // Act - version "1.02" has no authoritative CSV catalog
        var builder = await _service.CreateGameInstallationManifestAsync(
            installationPath, GameType.Generals, GameInstallationType.Retail, "1.02");
        var manifest = builder.Build();

        // Assert - symlinks/reparse points are excluded from manifest, including symlinked primary executable
        Assert.NotNull(manifest);
        Assert.Contains(manifest.Files, f => f.RelativePath == "normal.ini");
        Assert.DoesNotContain(manifest.Files, f => f.RelativePath == "generals.exe");
        Assert.DoesNotContain(manifest.Files, f => f.RelativePath == "linked_file.big");
        Assert.DoesNotContain(manifest.Files, f => f.RelativePath.Contains("outside.big"));
    }

    /// <summary>
    /// Tests that CreateGameInstallationManifestAsync resolves int-like versions consistently between basic info and catalog.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateGameInstallationManifestAsync_IntLikeVersion_ResolvesVersionConsistentlyAsync()
    {
        // Arrange
        var installationPath = Path.Combine(_tempDirectory, "IntVersionInstall");
        Directory.CreateDirectory(installationPath);

        await File.WriteAllTextAsync(Path.Combine(installationPath, "generals.exe"), "exe");
        await File.WriteAllTextAsync(Path.Combine(installationPath, "AudioEnglish.big"), "audio");

        // Act - pass version "0" (int default)
        var builder = await _service.CreateGameInstallationManifestAsync(
            installationPath, GameType.Generals, GameInstallationType.Steam, "0", "EN");
        var manifest = builder.Build();

        // Assert - version in manifest should be resolved to 1.08
        Assert.NotNull(manifest);
        Assert.Equal("1.08", manifest.Version);
    }

    /// <summary>
    /// Cleans up temporary test files.
    /// </summary>
    public void Dispose()
    {
        FileOperationsService.DeleteDirectoryIfExists(_tempDirectory);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Prepares a dummy executable file for testing.
    /// </summary>
    /// <returns>A tuple containing the client path and executable path.</returns>
    private async Task<(string ClientPath, string ExecutablePath)> PrepareDummyExeAsync()
    {
        var clientPath = Path.Combine(_tempDirectory, "TestClient");
        Directory.CreateDirectory(clientPath);
        var executablePath = Path.Combine(clientPath, "generals.exe");
        await File.WriteAllTextAsync(executablePath, "dummy exe");
        return (clientPath, executablePath);
    }
}
