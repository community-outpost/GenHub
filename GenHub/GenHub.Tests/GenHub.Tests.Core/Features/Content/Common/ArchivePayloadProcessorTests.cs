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
        var bigHeader = new byte[] { (byte)'B', (byte)'I', (byte)'G', (byte)'F', 0x00, 0x10, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00 };
        await File.WriteAllBytesAsync(gibPath, bigHeader);

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
        var bigHeader = new byte[] { (byte)'B', (byte)'I', (byte)'G', (byte)'F', 0x00, 0x10, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00 };
        await File.WriteAllBytesAsync(ctrPath, bigHeader);

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

    /// <summary>
    /// Verifies that archive normalization safely distinguishes between real BIG archives and MZ disguised executables.
    /// Real BIG archives become .big, whereas MZ executables become .exe and are never named .big.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_WithDisguisedExecutableAndBigArchive_NormalizesSafelyAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);

        // Disguised executable (MZ header) named generals.ctr
        var exeCtrPath = Path.Combine(_stagingDirectory, "generals.ctr");
        var mzBytes = new byte[] { (byte)'M', (byte)'Z', 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00 };
        await File.WriteAllBytesAsync(exeCtrPath, mzBytes);

        // Real BIG archive (BIGF header) named !Contra.ctr
        var bigCtrPath = Path.Combine(_stagingDirectory, "!Contra.ctr");
        var bigBytes = new byte[] { (byte)'B', (byte)'I', (byte)'G', (byte)'F', 0x00, 0x10, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00 };
        await File.WriteAllBytesAsync(bigCtrPath, bigBytes);

        var processor = CreateProcessor();

        // Act
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.Mod, GameType.ZeroHour, normalizeInactiveArchives: true);

        // Assert
        Assert.True(File.Exists(Path.Combine(_stagingDirectory, "!Contra.big")), "!Contra.ctr with BIGF magic should become !Contra.big");
        Assert.False(File.Exists(Path.Combine(_stagingDirectory, "!Contra.ctr")), "!Contra.ctr should no longer exist");

        Assert.True(File.Exists(Path.Combine(_stagingDirectory, "generals.exe")), "generals.ctr with MZ magic should become generals.exe");
        Assert.False(File.Exists(Path.Combine(_stagingDirectory, "generals.big")), "generals.ctr MUST NEVER become generals.big");
        Assert.False(File.Exists(Path.Combine(_stagingDirectory, "generals.ctr")), "generals.ctr should no longer exist");
    }

    /// <summary>
    /// Verifies that Smart Install Maker executables with BZip2 and ZLib streams, uninstaller entries, and .ctr archives
    /// are successfully unpacked and normalized without requiring external assets.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExtractArchivesSafelyAsync_WithSyntheticSmartInstallMakerExecutable_ExtractsAndNormalizesSuccessfully()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var bigHeader = new byte[] { (byte)'B', (byte)'I', (byte)'G', (byte)'F', 0x10, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00 };
        var bigPayload = System.Text.Encoding.ASCII.GetBytes("TestIniDataInsideBig");
        var bigContent = bigHeader.Concat(bigPayload).ToArray();

        var exeHeader = new byte[] { (byte)'M', (byte)'Z', 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00 };
        var exePayload = System.Text.Encoding.ASCII.GetBytes("LauncherCode");
        var exeContent = exeHeader.Concat(exePayload).ToArray();

        var iniContent = System.Text.Encoding.ASCII.GetBytes("GameData=1\r\nVersion=1.0\r\n");

        var syntheticSimBytes = CreateSyntheticSmartInstallMakerExecutable(
        [
            ("!ContraData.ctr", bigContent, true),
            ("Contra_Launcher.exe", exeContent, true),
            ("Data/INI/GameData.ini", iniContent, false),
        ],
        includeUninstallerEntry: true);

        var installerPath = Path.Combine(_stagingDirectory, "ContraXBeta2Setup.exe");
        await File.WriteAllBytesAsync(installerPath, syntheticSimBytes);

        var processor = CreateProcessor();

        // Act: 1. Extract archive safely
        await processor.ExtractArchivesSafelyAsync(_stagingDirectory, ContentType.Mod);

        // Assert: installer executable should be deleted after successful extraction
        Assert.False(File.Exists(installerPath), "Installer executable should be deleted after extraction.");

        // Act: 2. Normalize directory structure
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.Mod, GameType.ZeroHour, normalizeInactiveArchives: true);

        // Assert: extracted files exist and .ctr is normalized to .big
        Assert.True(File.Exists(Path.Combine(_stagingDirectory, "!ContraData.big")), "Expected !ContraData.ctr to be normalized to !ContraData.big");
        Assert.True(File.Exists(Path.Combine(_stagingDirectory, "Contra_Launcher.exe")), "Expected Contra_Launcher.exe to exist");
        Assert.True(File.Exists(Path.Combine(_stagingDirectory, "Data", "INI", "GameData.ini")), "Expected Data/INI/GameData.ini to exist");
        Assert.False(File.Exists(Path.Combine(_stagingDirectory, "ModUninstaller.exe")), "Uninstaller executable should not be extracted");
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

    private static byte[] CreateSyntheticSmartInstallMakerExecutable(
        (string Name, byte[] Content, bool UseBzip2)[] files,
        bool includeUninstallerEntry = true)
    {
        using var ms = new MemoryStream();

        // 1. DOS Header (64 bytes)
        var dosHeader = new byte[64];
        dosHeader[0] = (byte)'M';
        dosHeader[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(dosHeader, 0x3C); // e_lfanew = 0x80
        ms.Write(dosHeader, 0, 64);

        // Pad to 0x80 (128 bytes)
        while (ms.Length < 0x80)
        {
            ms.WriteByte(0);
        }

        // 2. PE Header at 0x80
        ms.Write([(byte)'P', (byte)'E', 0, 0]);
        var coffHeader = new byte[20];
        BitConverter.GetBytes((ushort)0x14C).CopyTo(coffHeader, 0);
        BitConverter.GetBytes((ushort)1).CopyTo(coffHeader, 2);
        BitConverter.GetBytes((ushort)0).CopyTo(coffHeader, 16);
        BitConverter.GetBytes((ushort)0x102).CopyTo(coffHeader, 18);
        ms.Write(coffHeader, 0, 20);

        // Section header (40 bytes): Name=.text, VirtualSize=0x200, VirtualAddress=0x1000, SizeOfRawData=0x200, PointerToRawData=0x200
        var secHeader = new byte[40];
        System.Text.Encoding.ASCII.GetBytes(".text").CopyTo(secHeader, 0);
        BitConverter.GetBytes(0x200).CopyTo(secHeader, 8);
        BitConverter.GetBytes(0x1000).CopyTo(secHeader, 12);
        BitConverter.GetBytes(0x200).CopyTo(secHeader, 16);
        BitConverter.GetBytes(0x200).CopyTo(secHeader, 20);
        ms.Write(secHeader, 0, 40);

        // Pad to Raw End = 0x200 + 0x200 = 0x400 (1024 bytes)
        while (ms.Length < 0x400)
        {
            ms.WriteByte(0);
        }

        // Overlay starts at 0x400 (1024)
        var simSig = new byte[] { 0x77, 0x77, 0x67, 0x54, 0x29, 0x48, 0x35, 0x14 };
        ms.Write(simSig, 0, simSig.Length);

        // Prepare compressed payloads
        using var payloadMs = new MemoryStream();
        var uninstallerText = System.Text.Encoding.Latin1.GetBytes("UninstallerStubText");
        payloadMs.Write(uninstallerText, 0, uninstallerText.Length);

        var tableRecords = new List<(string Name, uint UncompSize, uint Offset, uint CompSize)>();

        if (includeUninstallerEntry)
        {
            tableRecords.Add(("ModUninstaller.exe", 100, 0, (uint)uninstallerText.Length));
        }

        foreach (var (name, content, useBzip2) in files)
        {
            var offset = (uint)payloadMs.Length;
            byte[] compressed;
            if (useBzip2)
            {
                using var bzMs = new MemoryStream();
                using (var bz = SharpCompress.Compressors.BZip2.BZip2Stream.Create(bzMs, SharpCompress.Compressors.CompressionMode.Compress, decompressConcatenated: false, leaveOpen: true))
                {
                    bz.Write(content, 0, content.Length);
                }

                compressed = bzMs.ToArray();
            }
            else
            {
                using var defMs = new MemoryStream();
                defMs.WriteByte(0x78);
                defMs.WriteByte(0xDA);
                using (var def = new DeflateStream(defMs, CompressionLevel.Optimal, leaveOpen: true))
                {
                    def.Write(content, 0, content.Length);
                }

                compressed = defMs.ToArray();
            }

            payloadMs.Write(compressed, 0, compressed.Length);
            tableRecords.Add((name, (uint)content.Length, offset, (uint)compressed.Length));
        }

        // Prepare table data
        using var tableMs = new MemoryStream();
        tableMs.Write(new byte[40]); // initial padding
        foreach (var (name, uncomp, offset, comp) in tableRecords)
        {
            var recordHeader = new byte[40];
            BitConverter.GetBytes(uncomp).CopyTo(recordHeader, 0);
            BitConverter.GetBytes(offset).CopyTo(recordHeader, 4);
            BitConverter.GetBytes(comp).CopyTo(recordHeader, 8);
            tableMs.Write(recordHeader, 0, 40);

            var nameBytes = System.Text.Encoding.Latin1.GetBytes(name + "\0");
            tableMs.Write(nameBytes, 0, nameBytes.Length);
            tableMs.Write(new byte[40]); // separator padding
        }

        byte[] compressedTable;
        using (var defTableMs = new MemoryStream())
        {
            defTableMs.WriteByte(0x78);
            defTableMs.WriteByte(0xDA);
            using (var def = new DeflateStream(defTableMs, CompressionLevel.Optimal, leaveOpen: true))
            {
                var tableRaw = tableMs.ToArray();
                def.Write(tableRaw, 0, tableRaw.Length);
            }

            compressedTable = defTableMs.ToArray();
        }

        // Block 0: Dummy Info Block
        byte[] dummyData = [0x78, 0xDA, 0x01, 0x00, 0x00, 0xFF, 0xFF];
        using var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write((short)1);
        writer.Write(dummyData.Length + 5);
        writer.Write(0);
        writer.Write((byte)1);
        writer.Write(dummyData);

        // Block 1: Table Block (second to last)
        writer.Write((int)1);
        writer.Write(compressedTable.Length + 5);
        writer.Write(0);
        writer.Write((byte)1);
        writer.Write(compressedTable);

        // Block 2: Payload Block (last)
        var payloadBytes = payloadMs.ToArray();
        writer.Write((int)2);
        writer.Write(payloadBytes.Length + 5);
        writer.Write(0);
        writer.Write((byte)1);
        writer.Write(payloadBytes);

        return ms.ToArray();
    }
}
