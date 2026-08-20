using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using GenHub.Core.Models.Enums;
using GenHub.Features.Content.Services.Common;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Common;

/// <summary>
/// Unit tests for archive payload processing and directory structure normalization.
/// </summary>
public sealed class ArchivePayloadProcessorTests : IDisposable
{
    private readonly string _stagingDirectory = Path.Combine(Path.GetTempPath(), "GenHubPayloadTests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Verifies that extracting a valid ZIP archive unpacks all entries and removes the archive file.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExtractArchivesSafelyAsync_ValidZip_ExtractsAllEntriesAndDeletesZipAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var zipPath = Path.Combine(_stagingDirectory, "test.zip");
        {
            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            {
                using var writer1 = new StreamWriter(archive.CreateEntry("Data/INI/GameData.ini").Open());
                await writer1.WriteAsync("GameData=1");
            }

            {
                using var writer2 = new StreamWriter(archive.CreateEntry("Art/Textures/test.tga").Open());
                await writer2.WriteAsync("Texture");
            }
        }

        var processor = CreateProcessor();

        // Act
        await processor.ExtractArchivesSafelyAsync(_stagingDirectory);

        // Assert
        Assert.False(File.Exists(zipPath));
        Assert.True(File.Exists(Path.Combine(_stagingDirectory, "Data", "INI", "GameData.ini")));
        Assert.True(File.Exists(Path.Combine(_stagingDirectory, "Art", "Textures", "test.tga")));
    }

    /// <summary>
    /// Verifies that multi-level nested wrapper directories (e.g. ModDB mods like C&amp;C Generals Undone)
    /// are recursively flattened so game assets end up directly at the workspace root.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_MultiLevelSingleWrapper_FlattensToRootAsync()
    {
        // Arrange
        var nestedDir = Path.Combine(_stagingDirectory, "C&C Generals Undone v1.0", "C&C Generals Undone v1.0");
        Directory.CreateDirectory(Path.Combine(nestedDir, "Art", "Textures"));
        Directory.CreateDirectory(Path.Combine(nestedDir, "Data", "INI"));
        Directory.CreateDirectory(Path.Combine(nestedDir, "Window"));

        await File.WriteAllTextAsync(Path.Combine(nestedDir, "Readme.txt"), "Generals Undone Readme");
        await File.WriteAllTextAsync(Path.Combine(nestedDir, "Art", "Textures", "test.tga"), "texture data");
        await File.WriteAllTextAsync(Path.Combine(nestedDir, "Data", "INI", "GameData.ini"), "data");
        await File.WriteAllTextAsync(Path.Combine(nestedDir, "Window", "MainMenu.wnd"), "window");

        var processor = CreateProcessor();

        // Act
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.Mod, GameType.ZeroHour);

        // Assert
        Assert.True(File.Exists(Path.Combine(_stagingDirectory, "Readme.txt")));
        Assert.True(File.Exists(Path.Combine(_stagingDirectory, "Art", "Textures", "test.tga")));
        Assert.True(File.Exists(Path.Combine(_stagingDirectory, "Data", "INI", "GameData.ini")));
        Assert.True(File.Exists(Path.Combine(_stagingDirectory, "Window", "MainMenu.wnd")));

        // Old wrapper paths should no longer exist
        Assert.False(Directory.Exists(Path.Combine(_stagingDirectory, "C&C Generals Undone v1.0")));
    }

    /// <summary>
    /// Verifies that loose documentation files at root alongside a single mod wrapper directory
    /// are reconciled by promoting the mod contents to the root and keeping the documentation files.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_LooseReadmeWithModWrapper_FlattensModWrapperAlongsideReadmeAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        await File.WriteAllTextAsync(Path.Combine(_stagingDirectory, "Readme.txt"), "Important instructions");
        await File.WriteAllTextAsync(Path.Combine(_stagingDirectory, "ModDB_Link.url"), "https://www.moddb.com");

        var modDir = Path.Combine(_stagingDirectory, "GeneralsUndone");
        Directory.CreateDirectory(Path.Combine(modDir, "Data", "INI"));
        Directory.CreateDirectory(Path.Combine(modDir, "Art", "Textures"));
        await File.WriteAllTextAsync(Path.Combine(modDir, "Data", "INI", "GameData.ini"), "inidata");
        await File.WriteAllTextAsync(Path.Combine(modDir, "Art", "Textures", "unit.tga"), "tgadata");

        var processor = CreateProcessor();

        // Act
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.Mod, GameType.ZeroHour);

        // Assert
        Assert.True(File.Exists(Path.Combine(_stagingDirectory, "Readme.txt")));
        Assert.True(File.Exists(Path.Combine(_stagingDirectory, "ModDB_Link.url")));
        Assert.True(File.Exists(Path.Combine(_stagingDirectory, "Data", "INI", "GameData.ini")));
        Assert.True(File.Exists(Path.Combine(_stagingDirectory, "Art", "Textures", "unit.tga")));
        Assert.False(Directory.Exists(modDir));
    }

    /// <summary>
    /// Verifies that game-specific subdirectories matching the target game (e.g. "Zero Hour")
    /// are promoted to the payload root.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_GameSpecificSubdirectory_PromotesMatchingTargetGameFolderAsync()
    {
        // Arrange
        var zhDir = Path.Combine(_stagingDirectory, "Zero Hour", "Data", "INI");
        Directory.CreateDirectory(zhDir);
        await File.WriteAllTextAsync(Path.Combine(zhDir, "ZHData.ini"), "zh config");

        var processor = CreateProcessor();

        // Act
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.Mod, GameType.ZeroHour);

        // Assert
        Assert.True(File.Exists(Path.Combine(_stagingDirectory, "Data", "INI", "ZHData.ini")));
        Assert.False(Directory.Exists(Path.Combine(_stagingDirectory, "Zero Hour")));
    }

    /// <summary>
    /// Verifies that single map directories for ContentType.Map are preserved with their map folder.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_MapContent_PreservesSingleMapDirectoryAsync()
    {
        // Arrange
        var mapDir = Path.Combine(_stagingDirectory, "Lemuria");
        Directory.CreateDirectory(mapDir);
        await File.WriteAllTextAsync(Path.Combine(mapDir, "Lemuria.map"), "map payload");
        await File.WriteAllTextAsync(Path.Combine(mapDir, "Lemuria.tga"), "preview payload");

        var processor = CreateProcessor();

        // Act
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.Map, GameType.ZeroHour);

        // Assert
        Assert.True(Directory.Exists(mapDir));
        Assert.True(File.Exists(Path.Combine(mapDir, "Lemuria.map")));
        Assert.True(File.Exists(Path.Combine(mapDir, "Lemuria.tga")));
    }

    /// <summary>
    /// Verifies that double-wrapped map archives (e.g. MapDownload/MapName/MapName.map)
    /// strip only the outer wrapper while preserving the inner map folder.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_MapContentWithDoubleWrapper_FlattensOuterWrapperOnlyAsync()
    {
        // Arrange
        var outerWrapper = Path.Combine(_stagingDirectory, "MapDownloadWrapper");
        var mapDir = Path.Combine(outerWrapper, "Lemuria");
        Directory.CreateDirectory(mapDir);
        await File.WriteAllTextAsync(Path.Combine(mapDir, "Lemuria.map"), "map payload");
        await File.WriteAllTextAsync(Path.Combine(mapDir, "Lemuria.tga"), "preview payload");

        var processor = CreateProcessor();

        // Act
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.Map, GameType.ZeroHour);

        // Assert
        Assert.False(Directory.Exists(outerWrapper));
        Assert.True(Directory.Exists(Path.Combine(_stagingDirectory, "Lemuria")));
        Assert.True(File.Exists(Path.Combine(_stagingDirectory, "Lemuria", "Lemuria.map")));
    }

    /// <summary>
    /// Verifies that system junk files (.DS_Store, Thumbs.db, desktop.ini, __MACOSX)
    /// are purged during normalization.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_PurgesSystemJunkAsync()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_stagingDirectory, "__MACOSX"));
        Directory.CreateDirectory(Path.Combine(_stagingDirectory, "Data"));

        await File.WriteAllTextAsync(Path.Combine(_stagingDirectory, ".DS_Store"), "junk");
        await File.WriteAllTextAsync(Path.Combine(_stagingDirectory, "Thumbs.db"), "junk");
        await File.WriteAllTextAsync(Path.Combine(_stagingDirectory, "desktop.ini"), "junk");
        await File.WriteAllTextAsync(Path.Combine(_stagingDirectory, "__MACOSX", "._something"), "junk");
        await File.WriteAllTextAsync(Path.Combine(_stagingDirectory, "Data", "GameData.ini"), "real data");

        var processor = CreateProcessor();

        // Act
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.Mod, GameType.ZeroHour);

        // Assert
        Assert.False(File.Exists(Path.Combine(_stagingDirectory, ".DS_Store")));
        Assert.False(File.Exists(Path.Combine(_stagingDirectory, "Thumbs.db")));
        Assert.False(File.Exists(Path.Combine(_stagingDirectory, "desktop.ini")));
        Assert.False(Directory.Exists(Path.Combine(_stagingDirectory, "__MACOSX")));
        Assert.True(File.Exists(Path.Combine(_stagingDirectory, "Data", "GameData.ini")));
    }

    /// <summary>
    /// Verifies that an HTML error page pretending to be an archive is rejected.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExtractArchivesSafelyAsync_HtmlErrorPayload_ThrowsInvalidDataExceptionAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var fakeZip = Path.Combine(_stagingDirectory, "broken.zip");
        await File.WriteAllTextAsync(fakeZip, "<!DOCTYPE html><html><body>Error 404 Not Found</body></html>");

        var processor = CreateProcessor();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => processor.ExtractArchivesSafelyAsync(_stagingDirectory));
        Assert.Contains("HTML", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that a self-extracting .exe archive for a Mod is extracted safely and the source .exe is removed.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExtractArchivesSafelyAsync_SelfExtractingExeMod_ExtractsAndDeletesExeAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var sfxExePath = Path.Combine(_stagingDirectory, "ShockWaveV1201.exe");
        using (var archive = ZipFile.Open(sfxExePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("!ShockWave.big");
            using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("BIG data payload");
        }

        var processor = CreateProcessor();

        // Act
        await processor.ExtractArchivesSafelyAsync(_stagingDirectory, ContentType.Mod);

        // Assert
        Assert.False(File.Exists(sfxExePath));
        Assert.True(File.Exists(Path.Combine(_stagingDirectory, "!ShockWave.big")));
    }

    /// <summary>
    /// Verifies that executable files for tools or executables are never extracted or deleted even if they are zip containers.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExtractArchivesSafelyAsync_ExecutableTool_DoesNotExtractOrDeleteExeAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var toolExePath = Path.Combine(_stagingDirectory, "WorldBuilder.exe");
        using (var archive = ZipFile.Open(toolExePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("internal.dll");
            using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("dll");
        }

        var processor = CreateProcessor();

        // Act
        await processor.ExtractArchivesSafelyAsync(_stagingDirectory, ContentType.ModdingTool);

        // Assert: Tool executable is preserved intact and NOT extracted
        Assert.True(File.Exists(toolExePath));
        Assert.False(File.Exists(Path.Combine(_stagingDirectory, "internal.dll")));
    }

    /// <summary>
    /// Verifies that non-archive game.dat files are skipped and preserved.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExtractArchivesSafelyAsync_GameDatBinary_PreservedWithoutThrowingAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var gameDatPath = Path.Combine(_stagingDirectory, "game.dat");
        await File.WriteAllTextAsync(gameDatPath, "MZ_Binary_Executable_Payload_Not_Archive");

        var processor = CreateProcessor();

        // Act
        await processor.ExtractArchivesSafelyAsync(_stagingDirectory, ContentType.Patch);

        // Assert
        Assert.True(File.Exists(gameDatPath));
    }

    /// <summary>
    /// Verifies that valid .dat archives (e.g. 10zh.dat) are extracted.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExtractArchivesSafelyAsync_ValidDatArchive_ExtractsAndDeletesDatAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var datArchivePath = Path.Combine(_stagingDirectory, "10zh.dat");
        using (var archive = ZipFile.Open(datArchivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("ZH/game.dat");
            using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("ZH game binary");
        }

        var processor = CreateProcessor();

        // Act
        await processor.ExtractArchivesSafelyAsync(_stagingDirectory, ContentType.Patch);

        // Assert
        Assert.False(File.Exists(datArchivePath));
        Assert.True(File.Exists(Path.Combine(_stagingDirectory, "ZH", "game.dat")));
    }

    /// <summary>
    /// Verifies that inactive .gib mod files are renamed to .big during normalization.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_GibFiles_NormalizesToBigAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var gibPath = Path.Combine(_stagingDirectory, "!ShwAudio.gib");
        await File.WriteAllTextAsync(gibPath, "Audio BIG payload");

        var processor = CreateProcessor();

        // Act
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.Mod, GameType.ZeroHour);

        // Assert
        Assert.False(File.Exists(gibPath));
        Assert.True(File.Exists(Path.Combine(_stagingDirectory, "!ShwAudio.big")));
    }

    /// <summary>
    /// Verifies that inactive .ctr mod files (e.g. Contra) are renamed to .big during default normalization.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_CtrFiles_NormalizesToBigAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var ctrPath = Path.Combine(_stagingDirectory, "!ContraXBeta2_INI.ctr");
        await File.WriteAllTextAsync(ctrPath, "Contra INI payload");

        var processor = CreateProcessor();

        // Act
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.Mod, GameType.ZeroHour);

        // Assert
        Assert.False(File.Exists(ctrPath));
        Assert.True(File.Exists(Path.Combine(_stagingDirectory, "!ContraXBeta2_INI.big")));
    }

    /// <summary>
    /// Verifies that when normalizeInactiveArchives is false, .ctr and .gib files are preserved intact for Launcher Flow.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_WithNormalizeInactiveArchivesFalse_PreservesCtrAndGibFilesAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var ctrPath = Path.Combine(_stagingDirectory, "!ContraXBeta2_INI.ctr");
        var gibPath = Path.Combine(_stagingDirectory, "!ROTRAudio.gib");
        await File.WriteAllTextAsync(ctrPath, "Contra INI");
        await File.WriteAllTextAsync(gibPath, "ROTR Audio");

        var processor = CreateProcessor();

        // Act
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.Mod, GameType.ZeroHour, normalizeInactiveArchives: false);

        // Assert
        Assert.True(File.Exists(ctrPath));
        Assert.True(File.Exists(gibPath));
        Assert.False(File.Exists(Path.Combine(_stagingDirectory, "!ContraXBeta2_INI.big")));
        Assert.False(File.Exists(Path.Combine(_stagingDirectory, "!ROTRAudio.big")));
    }

    /// <summary>
    /// Verifies that self-extracting executable archives (e.g. ShockWaveV1201.exe with PE header followed by ZIP central directory)
    /// are detected and extracted safely for mod content types.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExtractArchivesSafelyAsync_SelfExtractingExeArchive_ExtractsAndDeletesExeAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var sfxExePath = Path.Combine(_stagingDirectory, "ShockWaveV1201.exe");

        using (var memoryStream = new MemoryStream())
        {
            var peHeader = new byte[512];
            peHeader[0] = 0x4D; // 'M'
            peHeader[1] = 0x5A; // 'Z'
            memoryStream.Write(peHeader, 0, peHeader.Length);

            using (var zipArchive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                {
                    var entry1 = zipArchive.CreateEntry("Data/INI/ShockWave.ini");
                    using var writer1 = new StreamWriter(entry1.Open());
                    await writer1.WriteAsync("ModName=ShockWave");
                }

                {
                    var entry2 = zipArchive.CreateEntry("!ShwAudio.gib");
                    using var writer2 = new StreamWriter(entry2.Open());
                    await writer2.WriteAsync("Audio content");
                }
            }

            await File.WriteAllBytesAsync(sfxExePath, memoryStream.ToArray());
        }

        var processor = CreateProcessor();

        // Act
        await processor.ExtractArchivesSafelyAsync(_stagingDirectory, ContentType.Mod);

        // Assert
        Assert.False(File.Exists(sfxExePath));
        Assert.True(File.Exists(Path.Combine(_stagingDirectory, "Data", "INI", "ShockWave.ini")));
        Assert.True(File.Exists(Path.Combine(_stagingDirectory, "!ShwAudio.gib")));
    }

    /// <summary>
    /// Verifies that Smart Install Maker SFX executables (e.g. ShockWave) are safely extracted and normalized.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExtractArchivesSafelyAsync_WithSmartInstallMakerExecutable_ExtractsAndNormalizesSuccessfully()
    {
        var casPath = @"A:\Steam\steamapps\common\.genhub-cas\objects\f4\f45e14d6b4a1e6e6feaa2ad737528b385586ad81ab7535bf9a330972db834c4e";
        if (!File.Exists(casPath))
        {
            return;
        }

        var testDir = Path.Combine(_stagingDirectory, "sim_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);

        var installerPath = Path.Combine(testDir, "ShockWaveV1201.exe");
        File.Copy(casPath, installerPath, overwrite: true);

        var processor = CreateProcessor();

        // 1. Extract archive safely
        await processor.ExtractArchivesSafelyAsync(testDir, ContentType.Mod);

        // 2. Original installer .exe should have been deleted after extraction
        Assert.False(File.Exists(installerPath), "Installer executable should be removed after successful extraction.");

        // 3. Normalize directory structure
        await processor.NormalizeDirectoryStructureAsync(testDir, ContentType.Mod, GameType.ZeroHour);

        // 4. Verify extracted and normalized game files exist with full uncompressed size
        var textureBigPath = Path.Combine(testDir, "!ShwTextures.big");
        Assert.True(File.Exists(textureBigPath), "Expected !ShwTextures.big to exist after normalization.");
        var textureInfo = new FileInfo(textureBigPath);
        Assert.True(textureInfo.Length > 60_000_000, $"Expected full textures >60MB, got {textureInfo.Length} bytes.");

        Assert.True(
            File.Exists(Path.Combine(testDir, "!!0ShwPtchIcon.big")),
            "Expected !!0ShwPtchIcon.big to exist.");
        Assert.True(
            File.Exists(Path.Combine(testDir, "!ShwAudio.big")),
            "Expected !ShwAudio.big to exist.");
        Assert.True(
            File.Exists(Path.Combine(testDir, "ShockWaveLauncher.exe")),
            "Expected ShockWaveLauncher.exe to exist.");
    }

    /// <summary>
    /// Verifies that payloads containing nested archives exceeding maximum extraction depth throw InvalidDataException.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExtractArchivesSafelyAsync_ExceedsMaxNestedDepth_ThrowsInvalidDataExceptionAsync()
    {
        // Arrange: create 6 layers of nested zips
        Directory.CreateDirectory(_stagingDirectory);
        var currentZip = Path.Combine(_stagingDirectory, "nested_level_6.zip");
        {
            using var archive = ZipFile.Open(currentZip, ZipArchiveMode.Create);
            using var writer = new StreamWriter(archive.CreateEntry("Data/test.ini").Open());
            await writer.WriteAsync("data=1");
        }

        for (var i = 5; i >= 1; i--)
        {
            var nextZip = Path.Combine(_stagingDirectory, $"nested_level_{i}.zip");
            using (var archive = ZipFile.Open(nextZip, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(currentZip, Path.GetFileName(currentZip));
            }

            File.Delete(currentZip);
            currentZip = nextZip;
        }

        var processor = CreateProcessor();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ExtractArchivesSafelyAsync(_stagingDirectory));
    }

    /// <summary>
    /// Verifies that wrapper promotion with colliding files preserving both files when content differs.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_WrapperCollisionWithDifferentContent_PreservesBothFilesAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var wrapperDir = Path.Combine(_stagingDirectory, "WrapperFolder");
        Directory.CreateDirectory(Path.Combine(wrapperDir, "Data"));

        // File at root
        await File.WriteAllTextAsync(Path.Combine(_stagingDirectory, "Readme.txt"), "Root Readme content");

        // File inside wrapper with same name but different content
        await File.WriteAllTextAsync(Path.Combine(wrapperDir, "Readme.txt"), "Wrapper Readme content");
        await File.WriteAllTextAsync(Path.Combine(wrapperDir, "Data", "GameData.ini"), "data=1");

        var processor = CreateProcessor();

        // Act
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.Mod, GameType.ZeroHour);

        // Assert
        Assert.True(File.Exists(Path.Combine(_stagingDirectory, "Readme.txt")));
        Assert.True(File.Exists(Path.Combine(_stagingDirectory, "Readme_1.txt")));
        var rootText = await File.ReadAllTextAsync(Path.Combine(_stagingDirectory, "Readme.txt"));
        var wrapperText = await File.ReadAllTextAsync(Path.Combine(_stagingDirectory, "Readme_1.txt"));
        Assert.Contains("Readme content", rootText);
        Assert.Contains("Readme content", wrapperText);
        Assert.NotEqual(rootText, wrapperText);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_stagingDirectory))
        {
            Directory.Delete(_stagingDirectory, recursive: true);
        }
    }

    private static ArchivePayloadProcessor CreateProcessor()
    {
        return new ArchivePayloadProcessor(new Mock<ILogger<ArchivePayloadProcessor>>().Object);
    }
}
