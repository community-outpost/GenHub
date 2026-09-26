using GenHub.Core.Constants;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Utilities;
using GenHub.Features.Content.Services.Common;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Common;

/// <summary>
/// Unit tests for archive payload processing and directory structure normalization.
/// </summary>
public sealed class ArchivePayloadProcessorTests : IDisposable
{
    private readonly string _stagingDirectory = Path.Combine(Path.GetTempPath(), "GenHubPayloadTests", Guid.NewGuid().ToString("N"));

    private sealed class SynchronousProgress<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }

    /// <summary>
    /// Verifies that an HTML error document named with a .tar extension is identified and throws an InvalidDataException.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExtractArchivesSafelyAsync_HtmlErrorSavedAsTar_ThrowsInvalidDataExceptionAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var tarPath = Path.Combine(_stagingDirectory, "expired_download.tar");
        await File.WriteAllTextAsync(tarPath, "<!DOCTYPE html><html><body>Link Expired</body></html>");

        var processor = CreateProcessor();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ExtractArchivesSafelyAsync(_stagingDirectory, ContentType.Mod));
        Assert.Contains("Downloaded file is HTML or web error text, not an archive", ex.Message);
    }

    /// <summary>
    /// Verifies that an invalid XZ file without proper magic bytes throws an InvalidDataException.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExtractArchivesSafelyAsync_InvalidXzArchive_ThrowsInvalidDataExceptionAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var xzPath = Path.Combine(_stagingDirectory, "corrupted.xz");
        await File.WriteAllBytesAsync(xzPath, new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 });

        var processor = CreateProcessor();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ExtractArchivesSafelyAsync(_stagingDirectory, ContentType.Mod));
        Assert.Contains("is not a valid XZ archive", ex.Message);
    }

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
    /// Verifies that an archive located in a subfolder extracts its contents into that subfolder,
    /// rather than flattening into the root payload directory.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExtractArchivesSafelyAsync_NestedArchiveInSubfolder_ExtractsIntoSubfolderAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var subDir = Path.Combine(_stagingDirectory, "Maps");
        Directory.CreateDirectory(subDir);
        var subZip = Path.Combine(subDir, "campaign.zip");

        using (var archive = ZipFile.Open(subZip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("campaign.map");
            using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("map-content");
        }

        var processor = CreateProcessor();

        // Act
        await processor.ExtractArchivesSafelyAsync(_stagingDirectory);

        // Assert: campaign.map should be in Maps/, not in _stagingDirectory
        Assert.False(File.Exists(subZip));
        Assert.True(File.Exists(Path.Combine(subDir, "campaign.map")));
        Assert.False(File.Exists(Path.Combine(_stagingDirectory, "campaign.map")));
    }

    /// <summary>
    /// Verifies that an archive exceeding the maximum allowed entry count throws an InvalidDataException.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExtractArchivesSafelyAsync_ExceedsMaxZipEntryCount_ThrowsInvalidDataExceptionAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var zipPath = Path.Combine(_stagingDirectory, "many_entries.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            for (var i = 0; i <= CatalogConstants.MaxZipEntryCount; i++)
            {
                archive.CreateEntry($"file_{i}.txt");
            }
        }

        var processor = CreateProcessor();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ExtractArchivesSafelyAsync(_stagingDirectory));
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
    /// Verifies that an HTML document with leading comment headers (e.g. OneDrive login page) throws an InvalidDataException.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExtractArchivesSafelyAsync_HtmlCommentHeaderFile_ThrowsInvalidDataExceptionAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var fakeZip = Path.Combine(_stagingDirectory, "mod.zip");
        await File.WriteAllTextAsync(
            fakeZip,
            "<!-- Copyright (C) Microsoft Corporation. All rights reserved. -->\n<!DOCTYPE html><html><head><title>Sign in to your account</title></head><body>login.live.com</body></html>");

        var processor = CreateProcessor();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => processor.ExtractArchivesSafelyAsync(_stagingDirectory));
        Assert.Contains("HTML", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that a file named as a ZIP archive without PK magic bytes throws an InvalidDataException before extraction.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExtractArchivesSafelyAsync_InvalidZipMagicBytes_ThrowsInvalidDataExceptionAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var fakeZip = Path.Combine(_stagingDirectory, "mod.zip");
        await File.WriteAllBytesAsync(fakeZip, [0x00, 0x01, 0x02, 0x03, 0x04]);

        var processor = CreateProcessor();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => processor.ExtractArchivesSafelyAsync(_stagingDirectory));
        Assert.Contains("not a valid ZIP archive", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that an archive served under another format's extension is still extracted from its content.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExtractArchivesSafelyAsync_SevenZipArchiveNamedZip_ExtractsContentAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var mislabelled = Path.Combine(_stagingDirectory, "mod.zip");
        await File.WriteAllBytesAsync(
            mislabelled,
            Convert.FromBase64String(
                "N3q8ryccAAQE0DRtEwAAAAAAAABSAAAAAAAAAKnyu85taXNsYWJlbGxlZCBhcmNoaXZlAQQGAAEJEwAHCwEAAQEADBMACAoB9b6xRQAABQERFwByAGUAYQBkAG0AZQAuAHQAeAB0AAAAGQQAAAAAFAoBAAA9glISTd0BFQYBACCApIEAAA=="));

        var processor = CreateProcessor();

        // Act
        await processor.ExtractArchivesSafelyAsync(_stagingDirectory);

        // Assert
        var extracted = Path.Combine(_stagingDirectory, "readme.txt");
        Assert.True(File.Exists(extracted));
        Assert.Equal("mislabelled archive", await File.ReadAllTextAsync(extracted));
        Assert.False(File.Exists(mislabelled));
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
        {
            using var archive = ZipFile.Open(datArchivePath, ZipArchiveMode.Create);
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
    [Fact(Skip = "Requires local SmartInstallMaker SFX payload fixture from CAS store")]
    public async Task ExtractArchivesSafelyAsync_WithSmartInstallMakerExecutable_ExtractsAndNormalizesSuccessfully()
    {
        var casPath = @"A:\Steam\steamapps\common\.genhub-cas\objects\f4\f45e14d6b4a1e6e6feaa2ad737528b385586ad81ab7535bf9a330972db834c4e";
        Assert.True(File.Exists(casPath), "Expected test CAS object fixture to exist when running local SIM fixture test.");

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
    /// Verifies that archives containing directory traversal entries (Zip Slip) throw <see cref="InvalidDataException"/>.
    /// </summary>
    /// <param name="maliciousEntryName">The malicious entry path.</param>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("../../evil.txt")]
    [InlineData("sub/../../evil.txt")]
    [InlineData("/evil.txt")]
    public async Task ExtractArchivesSafelyAsync_WithZipSlipEntry_ThrowsInvalidDataExceptionAsync(string maliciousEntryName)
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var zipPath = Path.Combine(_stagingDirectory, "malicious.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry(maliciousEntryName);
            using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("malicious content");
        }

        var processor = CreateProcessor();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ExtractArchivesSafelyAsync(_stagingDirectory));
    }

    /// <summary>
    /// Verifies that self-extracting zip .exe archives containing directory traversal entries (Zip Slip)
    /// throw <see cref="InvalidDataException"/> reporting unsafe path.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExtractArchivesSafelyAsync_SelfExtractingExeWithZipSlipEntry_ThrowsInvalidDataExceptionWithUnsafePathAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var sfxExePath = Path.Combine(_stagingDirectory, "malicious_mod.exe");

        using (var archive = ZipFile.Open(sfxExePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../evil.txt");
            using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("malicious content");
        }

        var processor = CreateProcessor();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            processor.ExtractArchivesSafelyAsync(_stagingDirectory, ContentType.Mod));
        Assert.Contains("unsafe path", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that cancelling extraction of a self-extracting .exe mid-extraction rethrows <see cref="OperationCanceledException"/>
    /// rather than converting it to an InvalidDataException.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExtractArchivesSafelyAsync_SelfExtractingExeWithCancellation_RethrowsOperationCanceledExceptionAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var sfxExePath = Path.Combine(_stagingDirectory, "mod.exe");

        using (var archive = ZipFile.Open(sfxExePath, ZipArchiveMode.Create))
        {
            var entry1 = archive.CreateEntry("game1.big");
            using (var writer1 = new StreamWriter(entry1.Open()))
            {
                await writer1.WriteAsync("payload1");
            }

            var entry2 = archive.CreateEntry("game2.big");
            using var writer2 = new StreamWriter(entry2.Open());
            await writer2.WriteAsync("payload2");
        }

        using var cts = new CancellationTokenSource();
        var progress = new SynchronousProgress<ContentAcquisitionProgress>(_ => cts.Cancel());

        var processor = CreateProcessor();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            processor.ExtractArchivesSafelyAsync(_stagingDirectory, ContentType.Mod, progress: progress, cancellationToken: cts.Token));
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
    public async Task ExtractArchivesSafelyAsync_WithSyntheticSmartInstallMakerExecutable_ExtractsAndNormalizesSuccessfullyAsync()
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

    /// <summary>
    /// Verifies that an archive containing an entry whose resolved destination path matches the archive itself
    /// throws an InvalidDataException to avoid sharing violations or self-overwrite corruption.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExtractArchivesSafelyAsync_EntryMatchesArchiveSelfPath_ThrowsInvalidDataExceptionAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var archivePath = Path.Combine(_stagingDirectory, "self.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("self.zip");
            using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("self content");
        }

        var processor = CreateProcessor();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => processor.ExtractArchivesSafelyAsync(_stagingDirectory));
        Assert.Contains("cannot overwrite the archive itself", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that modern Smart Install Maker payloads with raw uncompressed entries exceeding 64KB
    /// are fully copied without truncation.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExtractArchivesSafelyAsync_WithModernSimRawUncompressedStreamOver64KB_ExtractsFullPayload()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var largePayload = new byte[70000];
        for (var i = 0; i < largePayload.Length; i++)
        {
            largePayload[i] = (byte)(i % 251);
        }

        var syntheticSimBytes = CreateSyntheticSmartInstallMakerExecutable(
        [
            ("LargeData.dat", largePayload, false),
        ],
        includeUninstallerEntry: false);

        var installerPath = Path.Combine(_stagingDirectory, "SetupLarge.exe");
        await File.WriteAllBytesAsync(installerPath, syntheticSimBytes);

        var processor = CreateProcessor();

        // Act
        await processor.ExtractArchivesSafelyAsync(_stagingDirectory, ContentType.Mod);

        // Assert
        var extractedPath = Path.Combine(_stagingDirectory, "LargeData.dat");
        Assert.True(File.Exists(extractedPath), "LargeData.dat should be extracted.");
        var extractedBytes = await File.ReadAllBytesAsync(extractedPath);
        Assert.Equal(largePayload.Length, extractedBytes.Length);
        Assert.Equal(largePayload, extractedBytes);
    }

    /// <summary>
    /// Symlinks are dereferenced during normalization: file links become real copies of
    /// their targets so hashing and CAS ingestion see content, while dangling links are
    /// removed instead of breaking the pipeline.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_Symlinks_DereferencesAndRemovesDanglingAsync()
    {
        Directory.CreateDirectory(_stagingDirectory);
        var target = Path.Combine(_stagingDirectory, "target.bin");
        await File.WriteAllBytesAsync(target, [0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01, 0x01, 0x00]);
        var link = Path.Combine(_stagingDirectory, "link.bin");
        var dangling = Path.Combine(_stagingDirectory, "dangling.bin");

        try
        {
            File.CreateSymbolicLink(link, target);
            File.CreateSymbolicLink(dangling, Path.Combine(_stagingDirectory, "absent.bin"));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        {
            // Environments without symlink rights cannot exercise this path.
            return;
        }

        var processor = CreateProcessor();
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.GameClient, GameType.ZeroHour);

        Assert.True(File.Exists(link));
        Assert.False(File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint));
        Assert.Equal(await File.ReadAllBytesAsync(target), await File.ReadAllBytesAsync(link));
        Assert.False(File.Exists(dangling));
    }

    /// <summary>
    /// A dereferenced symlink hashes identically to its target, so both resolve to the
    /// same CAS object when the factory ingests the normalized payload.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_SymlinkTargetAndLink_ShareContentHashAsync()
    {
        Directory.CreateDirectory(_stagingDirectory);
        var target = Path.Combine(_stagingDirectory, "engine.bin");
        await File.WriteAllBytesAsync(target, [0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01, 0x01, 0x00]);
        var link = Path.Combine(_stagingDirectory, "engine-link.bin");

        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        {
            // Environments without symlink rights cannot exercise this path.
            return;
        }

        var processor = CreateProcessor();
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.GameClient, GameType.ZeroHour);

        Assert.False(File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint));

        var targetHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(target)));
        var linkHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(link)));
        Assert.Equal(targetHash, linkHash);
    }

    /// <summary>
    /// A cyclic directory link (loop -&gt; .) is removed during normalization so later
    /// recursive enumerations cannot follow it into an infinite loop.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_CyclicDirectoryLink_RemovesLinkAsync()
    {
        Directory.CreateDirectory(_stagingDirectory);
        var loop = Path.Combine(_stagingDirectory, "loop");

        try
        {
            Directory.CreateSymbolicLink(loop, ".");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        {
            // Environments without symlink rights cannot exercise this path.
            return;
        }

        var processor = CreateProcessor();
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.GameClient, GameType.ZeroHour);

        Assert.False(Directory.Exists(loop));
    }

    /// <summary>
    /// An interior directory link (framework Versions/Current) survives normalization.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_InteriorDirectoryLink_PreservedAsync()
    {
        Directory.CreateDirectory(_stagingDirectory);
        var target = Directory.CreateDirectory(Path.Combine(_stagingDirectory, "Versions", "A")).FullName;
        File.WriteAllText(Path.Combine(target, "lib.bin"), "payload");
        var link = Path.Combine(_stagingDirectory, "Versions", "Current");

        // A second top-level directory keeps wrapper-stripping from relocating Versions.
        var sibling = Directory.CreateDirectory(Path.Combine(_stagingDirectory, "Data")).FullName;
        File.WriteAllText(Path.Combine(sibling, "data.bin"), "payload");

        try
        {
            Directory.CreateSymbolicLink(link, "A");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        {
            // Environments without symlink rights cannot exercise this path.
            return;
        }

        var processor = CreateProcessor();
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.GameClient, GameType.ZeroHour);

        Assert.True(Directory.Exists(link));
    }

    /// <summary>
    /// A link whose final hop escapes through an interior directory link is removed.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_EscapeThroughInteriorLink_RemovedAsync()
    {
        Directory.CreateDirectory(_stagingDirectory);
        var outside = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GenHub_Outside_" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            File.WriteAllText(Path.Combine(outside, "secret.bin"), "outside");

            // The hop lives in a subdirectory so the file link is normalized while the
            // hop still exists: first-hop containment alone would copy outside content in.
            var sub = Directory.CreateDirectory(Path.Combine(_stagingDirectory, "sub")).FullName;
            var hop = Path.Combine(sub, "hop");
            var link = Path.Combine(_stagingDirectory, "link.bin");

            try
            {
                Directory.CreateSymbolicLink(hop, outside);
                File.CreateSymbolicLink(link, Path.Combine("sub", "hop", "secret.bin"));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                // Environments without symlink rights cannot exercise this path.
                return;
            }

            var processor = CreateProcessor();
            await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.GameClient, GameType.ZeroHour);

            Assert.False(File.Exists(link));
        }
        finally
        {
            if (Directory.Exists(outside))
            {
                Directory.Delete(outside, recursive: true);
            }
        }
    }

    /// <summary>
    /// Mutual directory links across subdirectories (a/link -> ../b and b/link2 -> ../a)
    /// are detected as a cycle and removed so downstream enumerations do not enter an infinite loop.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_MutualDirectorySymlinks_RemovesCyclicLinkAsync()
    {
        Directory.CreateDirectory(_stagingDirectory);
        var dirA = Directory.CreateDirectory(Path.Combine(_stagingDirectory, "dirA")).FullName;
        var dirB = Directory.CreateDirectory(Path.Combine(_stagingDirectory, "dirB")).FullName;
        File.WriteAllText(Path.Combine(dirA, "fileA.bin"), "payloadA");
        File.WriteAllText(Path.Combine(dirB, "fileB.bin"), "payloadB");

        var linkInA = Path.Combine(dirA, "toB");
        var linkInB = Path.Combine(dirB, "toA");

        try
        {
            Directory.CreateSymbolicLink(linkInA, dirB);
            Directory.CreateSymbolicLink(linkInB, dirA);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        {
            // Environments without symlink rights cannot exercise this path.
            return;
        }

        var processor = CreateProcessor();
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.GameClient, GameType.ZeroHour);

        // At least one of the links must have been removed to break the cycle
        var existsA = Directory.Exists(linkInA);
        var existsB = Directory.Exists(linkInB);
        Assert.False(existsA && existsB, "Mutual directory symlinks should not both be preserved as they form a cycle.");
    }

    /// <summary>
    /// A link whose path cannot be resolved due to link resolution failures is deleted.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_BrokenSymlinkChain_RemovesLinkAsync()
    {
        Directory.CreateDirectory(_stagingDirectory);
        var nonExistent = Path.Combine(_stagingDirectory, "does_not_exist");
        var link = Path.Combine(_stagingDirectory, "dangling_link");

        try
        {
            File.CreateSymbolicLink(link, nonExistent);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        {
            return;
        }

        var processor = CreateProcessor();
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.GameClient, GameType.ZeroHour);

        // File.Exists is false for a dangling link even when the link itself still
        // exists, so enumerate the parent: the entry must be gone entirely.
        Assert.DoesNotContain(
            Directory.GetFileSystemEntries(_stagingDirectory),
            entry => Path.GetFileName(entry).Equals("dangling_link", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A self-referential directory link is still detected as a cycle when the payload
    /// is addressed through a symlinked ancestor: the link location is canonicalized
    /// before comparison so lexical and canonical forms cannot mismatch.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_SymlinkedRootAncestor_DetectsCycleAsync()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString());
        var staging = Path.Combine(tempRoot, "staging");
        Directory.CreateDirectory(staging);
        var rootLink = Path.Combine(tempRoot, "rootlink");
        try
        {
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(staging, "loop"), ".");
                Directory.CreateSymbolicLink(rootLink, staging);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                return;
            }

            var processor = CreateProcessor();
            await processor.NormalizeDirectoryStructureAsync(rootLink, ContentType.GameClient, GameType.ZeroHour);

            Assert.DoesNotContain(
                Directory.GetFileSystemEntries(staging),
                entry => Path.GetFileName(entry).Equals("loop", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that EnsureValidArchivePayload accepts a valid ZIP archive even when an entry contains HTML.
    /// </summary>
    [Fact]
    public void EnsureValidArchivePayload_ValidZipWithHtmlEntry_Succeeds()
    {
        var tempZip = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = ZipFile.Open(tempZip, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("index.html", CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open());
                writer.Write("<!DOCTYPE html><html><head><title>Test</title></head><body><h1>Content</h1></body></html>");
            }

            // Act & Assert
            ArchivePayloadProcessor.EnsureValidArchivePayload(tempZip);
        }
        finally
        {
            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }
        }
    }

    /// <summary>
    /// Verifies that EnsureValidArchivePayload rejects an HTML error page masquerading as a ZIP file.
    /// </summary>
    [Fact]
    public void EnsureValidArchivePayload_HtmlErrorDocument_ThrowsInvalidDataException()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
        try
        {
            File.WriteAllText(tempFile, "<!DOCTYPE html><html><head><title>Error</title></head><body>Access Denied</body></html>");

            // Act & Assert
            var ex = Assert.Throws<InvalidDataException>(() => ArchivePayloadProcessor.EnsureValidArchivePayload(tempFile));
            Assert.Contains("HTML or web error text", ex.Message);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    /// <summary>
    /// Verifies that a complete RAR archive served as .zip is extracted by content and then removed.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExtractArchivesSafelyAsync_RarArchiveNamedZip_ExtractsContentAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var archivePath = Path.Combine(_stagingDirectory, "mod.zip");

        // Complete RAR5 archive with a stored readme.txt, header/data CRCs and an end marker.
        await File.WriteAllBytesAsync(archivePath, Convert.FromBase64String(
            "UmFyIRoHAQDFGjMyAwEAACYUNnUXAgIXBBcAr0Q9jwAACnJlYWRtZS50eHRtaXNsYWJlbGxlZCBSQVIgYXJjaGl2ZRmyOjUDBQAA"));

        // Act
        await CreateProcessor().ExtractArchivesSafelyAsync(_stagingDirectory);

        // Assert
        var extracted = Path.Combine(_stagingDirectory, "readme.txt");
        Assert.True(File.Exists(extracted));
        Assert.Equal("mislabelled RAR archive", await File.ReadAllTextAsync(extracted));
        Assert.False(File.Exists(archivePath));
    }

    /// <summary>
    /// Verifies that incomplete or invalid PK prefixes are rejected before extraction for every candidate format.
    /// </summary>
    /// <param name="extension">The candidate archive extension.</param>
    /// <param name="hex">The invalid header bytes.</param>
    [Theory]
    [InlineData(".zip", "504B")]
    [InlineData(".zip", "504B03")]
    [InlineData(".zip", "504B0000")]
    [InlineData(".7z", "504B0000")]
    [InlineData(".rar", "504B0000")]
    [InlineData(".gz", "504B0000")]
    [InlineData(".bz2", "504B0000")]
    [InlineData(".xz", "504B0000")]
    public void EnsureValidArchivePayload_InvalidPkPrefix_ThrowsInvalidDataException(string extension, string hex)
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var archivePath = Path.Combine(_stagingDirectory, "mod" + extension);
        File.WriteAllBytes(archivePath, Convert.FromHexString(hex));

        // Act & Assert
        Assert.Throws<InvalidDataException>(() => ArchivePayloadProcessor.EnsureValidArchivePayload(archivePath));
    }

    /// <summary>
    /// Verifies that an empty ZIP archive remains valid even when served under another extension.
    /// </summary>
    [Fact]
    public void EnsureValidArchivePayload_EmptyZipNamedRar_Succeeds()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var archivePath = Path.Combine(_stagingDirectory, "mod.rar");
        using (ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
        }

        // Act & Assert
        ArchivePayloadProcessor.EnsureValidArchivePayload(archivePath);
        Assert.True(ZipValidation.IsValidZipFile(archivePath));
    }

    /// <summary>
    /// Verifies that NormalizeMapPayloadStructure unwraps a lowercase "maps" directory
    /// even when a root readme prevents StripSingleWrapperDirectories.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_LowerCaseMapsDirectoryWithReadme_UnwrapsMapsDirectoryAsync()
    {
        Directory.CreateDirectory(_stagingDirectory);
        var readmePath = Path.Combine(_stagingDirectory, "README.txt");
        await File.WriteAllTextAsync(readmePath, "Readme content");

        var lowerMapsDir = Path.Combine(_stagingDirectory, "maps");
        var mapSubDir = Path.Combine(lowerMapsDir, "Desert");
        Directory.CreateDirectory(mapSubDir);
        var mapFilePath = Path.Combine(mapSubDir, "desert.map");
        await File.WriteAllTextAsync(mapFilePath, "map-data");

        var processor = CreateProcessor();
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.Map, GameType.ZeroHour);

        Assert.True(File.Exists(readmePath));
        Assert.False(Directory.Exists(lowerMapsDir));
        Assert.True(File.Exists(Path.Combine(_stagingDirectory, "Desert", "desert.map")));
    }

    /// <summary>
    /// Verifies that NormalizeMapPayloadStructure does not strip .map extensions from nested subdirectories.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_NestedDotMapDirectory_DoesNotStripNestedExtensionAsync()
    {
        Directory.CreateDirectory(_stagingDirectory);
        var mapDir = Path.Combine(_stagingDirectory, "Desert");
        var nestedMapDir = Path.Combine(mapDir, "Textures.map");
        Directory.CreateDirectory(nestedMapDir);
        await File.WriteAllTextAsync(Path.Combine(nestedMapDir, "texture.dds"), "dds-data");
        await File.WriteAllTextAsync(Path.Combine(mapDir, "Desert.map"), "map-data");

        var processor = CreateProcessor();
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.Map, GameType.ZeroHour);

        Assert.True(Directory.Exists(nestedMapDir));
        Assert.True(File.Exists(Path.Combine(nestedMapDir, "texture.dds")));
    }

    /// <summary>
    /// Verifies that NormalizeMapPayloadStructure does not fabricate a .tga preview in a directory lacking a .map file.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_TopLevelFolderWithoutMap_DoesNotFabricateTgaPreviewAsync()
    {
        Directory.CreateDirectory(_stagingDirectory);
        var mapDir = Path.Combine(_stagingDirectory, "Desert");
        Directory.CreateDirectory(mapDir);
        await File.WriteAllTextAsync(Path.Combine(mapDir, "Desert.map"), "map-data");

        var auxDir = Path.Combine(_stagingDirectory, "Textures");
        Directory.CreateDirectory(auxDir);
        var tgaPath = Path.Combine(auxDir, "preview.tga");
        await File.WriteAllTextAsync(tgaPath, "preview-data");

        var processor = CreateProcessor();
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.Map, GameType.ZeroHour);

        var fabricatedTga = Path.Combine(auxDir, "Textures.tga");
        Assert.False(File.Exists(fabricatedTga));
        Assert.True(File.Exists(tgaPath));
    }

    /// <summary>
    /// Verifies that single loose map normalization does not move unrelated files like readme.txt or metadata.ini
    /// into the map folder.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_SingleLooseMapWithUnrelatedFile_DoesNotMoveUnrelatedFileIntoMapFolderAsync()
    {
        Directory.CreateDirectory(_stagingDirectory);
        var mapPath = Path.Combine(_stagingDirectory, "Desert.map");
        var tgaPath = Path.Combine(_stagingDirectory, "Desert.tga");
        var readmePath = Path.Combine(_stagingDirectory, "readme.txt");
        var metadataPath = Path.Combine(_stagingDirectory, "metadata.ini");

        await File.WriteAllTextAsync(mapPath, "map-data");
        await File.WriteAllTextAsync(tgaPath, "tga-data");
        await File.WriteAllTextAsync(readmePath, "readme-content");
        await File.WriteAllTextAsync(metadataPath, "metadata-content");

        var processor = CreateProcessor();
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.Map, GameType.ZeroHour);

        var mapFolder = Path.Combine(_stagingDirectory, "Desert");
        Assert.True(File.Exists(Path.Combine(mapFolder, "Desert.map")));
        Assert.True(File.Exists(Path.Combine(mapFolder, "Desert.tga")));
        Assert.True(File.Exists(readmePath));
        Assert.True(File.Exists(metadataPath));
        Assert.False(File.Exists(Path.Combine(mapFolder, "readme.txt")));
        Assert.False(File.Exists(Path.Combine(mapFolder, "metadata.ini")));
    }

    /// <summary>
    /// Verifies that loose map organization deduplicates root map files when the target map already exists in subdirectory.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_DuplicateMapAtRootAndInSubdirectory_DeduplicatesLooseRootMapAsync()
    {
        Directory.CreateDirectory(_stagingDirectory);
        var targetFolder = Path.Combine(_stagingDirectory, "Desert");
        Directory.CreateDirectory(targetFolder);
        await File.WriteAllTextAsync(Path.Combine(targetFolder, "Desert.map"), "identical-data");
        await File.WriteAllTextAsync(Path.Combine(_stagingDirectory, "Desert.map"), "identical-data");
        await File.WriteAllTextAsync(Path.Combine(_stagingDirectory, "Desert.tga"), "preview-data");

        var processor = CreateProcessor();
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.Map, GameType.ZeroHour);

        Assert.False(File.Exists(Path.Combine(_stagingDirectory, "Desert.map")));
        Assert.True(File.Exists(Path.Combine(targetFolder, "Desert.map")));
        Assert.True(File.Exists(Path.Combine(targetFolder, "Desert.tga")));
    }

    /// <summary>
    /// Verifies that a loose map's .wak, map.ini and map.str companions move into the map folder with it.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_LooseMapWithWakIniAndStr_OrganizesAllCompanionsAsync()
    {
        Directory.CreateDirectory(_stagingDirectory);
        await File.WriteAllTextAsync(Path.Combine(_stagingDirectory, "Desert.map"), "map-data");
        await File.WriteAllTextAsync(Path.Combine(_stagingDirectory, "Desert.wak"), "wak-data");
        await File.WriteAllTextAsync(Path.Combine(_stagingDirectory, "map.ini"), "ini-data");
        await File.WriteAllTextAsync(Path.Combine(_stagingDirectory, "map.str"), "str-data");

        var processor = CreateProcessor();
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.Map, GameType.ZeroHour);

        var mapFolder = Path.Combine(_stagingDirectory, "Desert");
        Assert.Equal("wak-data", await File.ReadAllTextAsync(Path.Combine(mapFolder, "Desert.wak")));
        Assert.Equal("ini-data", await File.ReadAllTextAsync(Path.Combine(mapFolder, "map.ini")));
        Assert.Equal("str-data", await File.ReadAllTextAsync(Path.Combine(mapFolder, "map.str")));
        Assert.Empty(Directory.GetFiles(_stagingDirectory));
    }

    /// <summary>
    /// Verifies that shared companions retain their content in every map folder and leave no
    /// staging-root duplicate, regardless of filename casing.
    /// </summary>
    /// <param name="companionName">The shared companion filename.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData("map.ini")]
    [InlineData("MAP.INI")]
    [InlineData("map.str")]
    [InlineData("MAP.STR")]
    [InlineData("map.tga")]
    [InlineData("MAP.TGA")]
    public async Task NormalizeDirectoryStructureAsync_SharedCompanionCaseVariants_RemovesRootCopyAsync(string companionName)
    {
        Directory.CreateDirectory(_stagingDirectory);
        await File.WriteAllTextAsync(Path.Combine(_stagingDirectory, "Desert.map"), "desert-map");
        await File.WriteAllTextAsync(Path.Combine(_stagingDirectory, "Snow.map"), "snow-map");
        await File.WriteAllTextAsync(Path.Combine(_stagingDirectory, companionName), "shared-data");
        await File.WriteAllTextAsync(Path.Combine(_stagingDirectory, "unrelated.ini"), "unrelated-data");

        var processor = CreateProcessor();
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.Map, GameType.ZeroHour);

        foreach (var mapName in new[] { "Desert", "Snow" })
        {
            var targetName = companionName.Equals("map.tga", StringComparison.OrdinalIgnoreCase)
                ? mapName + Path.GetExtension(companionName)
                : companionName;
            Assert.Equal("shared-data", await File.ReadAllTextAsync(Path.Combine(_stagingDirectory, mapName, targetName)));
        }

        Assert.Equal("unrelated.ini", Path.GetFileName(Assert.Single(Directory.GetFiles(_stagingDirectory))));
        Assert.Equal("unrelated-data", await File.ReadAllTextAsync(Path.Combine(_stagingDirectory, "unrelated.ini")));
    }

    /// <summary>
    /// Shared companions may be deduplicated only when the destination has identical contents.
    /// Conflicts fail normalization while preserving both versions.
    /// </summary>
    /// <param name="companionName">The shared companion filename.</param>
    /// <param name="identical">Whether source and destination contents match.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData("map.ini", false)]
    [InlineData("map.str", false)]
    [InlineData("map.tga", false)]
    [InlineData("MAP.TGA", false)]
    [InlineData("MAP.INI", false)]
    [InlineData("MAP.STR", false)]
    [InlineData("map.ini", true)]
    [InlineData("map.str", true)]
    [InlineData("map.tga", true)]
    [InlineData("MAP.TGA", true)]
    [InlineData("MAP.INI", true)]
    [InlineData("MAP.STR", true)]
    public async Task NormalizeDirectoryStructureAsync_ExistingSharedCompanion_PreservesConflictingContentAsync(string companionName, bool identical)
    {
        Directory.CreateDirectory(_stagingDirectory);
        var mapFolder = Directory.CreateDirectory(Path.Combine(_stagingDirectory, "Desert")).FullName;
        await File.WriteAllTextAsync(Path.Combine(_stagingDirectory, "Desert.map"), "map-data");
        await File.WriteAllTextAsync(Path.Combine(mapFolder, "Desert.map"), "map-data");
        var source = Path.Combine(_stagingDirectory, companionName);
        var destination = Path.Combine(mapFolder, companionName.Equals("map.tga", StringComparison.OrdinalIgnoreCase) ? "Desert.tga" : companionName.ToLowerInvariant());
        await File.WriteAllTextAsync(source, "incoming");
        await File.WriteAllTextAsync(destination, identical ? "incoming" : "existing");

        var processor = CreateProcessor();
        if (identical)
        {
            await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.Map, GameType.ZeroHour);
            Assert.False(File.Exists(source));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.Map, GameType.ZeroHour));
            Assert.Equal("incoming", await File.ReadAllTextAsync(source));
        }

        Assert.Equal(identical ? "incoming" : "existing", await File.ReadAllTextAsync(destination));
    }

    /// <summary>
    /// Verifies that multiple loose maps sharing a root map.tga receive the thumbnail in each respective map folder.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_MultiLooseMapsWithSharedMapTga_CopiesPreviewToAllMapFoldersAsync()
    {
        Directory.CreateDirectory(_stagingDirectory);
        await File.WriteAllTextAsync(Path.Combine(_stagingDirectory, "Desert.map"), "desert-map");
        await File.WriteAllTextAsync(Path.Combine(_stagingDirectory, "Snow.map"), "snow-map");
        await File.WriteAllTextAsync(Path.Combine(_stagingDirectory, "map.tga"), "preview-data");

        var processor = CreateProcessor();
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.Map, GameType.ZeroHour);

        var desertTga = Path.Combine(_stagingDirectory, "Desert", "Desert.tga");
        var snowTga = Path.Combine(_stagingDirectory, "Snow", "Snow.tga");
        var rootTga = Path.Combine(_stagingDirectory, "map.tga");

        Assert.True(File.Exists(desertTga));
        Assert.True(File.Exists(snowTga));
        Assert.False(File.Exists(rootTga));
        Assert.False(File.Exists(Path.Combine(_stagingDirectory, "Desert", "map.tga")));
        Assert.False(File.Exists(Path.Combine(_stagingDirectory, "Snow", "map.tga")));
    }

    /// <summary>
    /// Verifies that when a loose map conflicts with a preexisting subdirectory map of the same name but different content,
    /// both maps are preserved by moving the loose map into a disambiguated directory.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_ConflictingLooseMapWithExistingSubdir_PreservesBothMapsInDisambiguatedFolderAsync()
    {
        Directory.CreateDirectory(_stagingDirectory);
        var targetFolder = Path.Combine(_stagingDirectory, "Desert");
        Directory.CreateDirectory(targetFolder);
        await File.WriteAllTextAsync(Path.Combine(targetFolder, "Desert.map"), "existing-content");
        await File.WriteAllTextAsync(Path.Combine(_stagingDirectory, "Desert.map"), "conflicting-content");

        var processor = CreateProcessor();
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.Map, GameType.ZeroHour);

        var disambiguatedFolder = Path.Combine(_stagingDirectory, "Desert (1)");
        Assert.False(File.Exists(Path.Combine(_stagingDirectory, "Desert.map")));
        Assert.True(File.Exists(Path.Combine(targetFolder, "Desert.map")));
        Assert.Equal("existing-content", await File.ReadAllTextAsync(Path.Combine(targetFolder, "Desert.map")));
        Assert.True(File.Exists(Path.Combine(disambiguatedFolder, "Desert.map")));
        Assert.Equal("conflicting-content", await File.ReadAllTextAsync(Path.Combine(disambiguatedFolder, "Desert.map")));
    }

    /// <summary>
    /// Verifies that loose map files with uppercase extensions like .MAP are properly organized into their own folders.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_UppercaseMapExtension_OrganizesCorrectlyAsync()
    {
        Directory.CreateDirectory(_stagingDirectory);
        var mapFile = Path.Combine(_stagingDirectory, "Arena.MAP");
        var tgaFile = Path.Combine(_stagingDirectory, "Arena.TGA");
        await File.WriteAllTextAsync(mapFile, "map-content");
        await File.WriteAllTextAsync(tgaFile, "tga-content");

        var processor = CreateProcessor();
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.Map, GameType.ZeroHour);

        var targetFolder = Path.Combine(_stagingDirectory, "Arena");
        Assert.False(File.Exists(mapFile));
        Assert.False(File.Exists(tgaFile));
        Assert.True(File.Exists(Path.Combine(targetFolder, "Arena.MAP")));
        Assert.True(File.Exists(Path.Combine(targetFolder, "Arena.TGA")));
    }

    /// <summary>
    /// Verifies that when a loose map conflicts with an existing directory, disambiguation avoids colliding
    /// with another loose map that owns the candidate disambiguated base name (e.g., "Desert (1).map").
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_ConflictingLooseMapWithExistingDisambiguatedBaseName_AvoidsDirectoryCollisionAsync()
    {
        // Arrange: Existing Desert/Desert.map, loose conflicting Desert.map, and loose Desert (1).map
        Directory.CreateDirectory(_stagingDirectory);
        var existingDesertDir = Path.Combine(_stagingDirectory, "Desert");
        Directory.CreateDirectory(existingDesertDir);
        var existingMapFile = Path.Combine(existingDesertDir, "Desert.map");
        await File.WriteAllTextAsync(existingMapFile, "existing-desert-map");

        var looseDesertMap = Path.Combine(_stagingDirectory, "Desert.map");
        await File.WriteAllTextAsync(looseDesertMap, "loose-conflicting-desert-map");

        var looseDesert1Map = Path.Combine(_stagingDirectory, "Desert (1).map");
        await File.WriteAllTextAsync(looseDesert1Map, "loose-desert-1-map");

        // Act
        var processor = CreateProcessor();
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.Map, GameType.ZeroHour);

        // Assert:
        // 1. Existing Desert/Desert.map is preserved.
        Assert.True(File.Exists(existingMapFile));
        Assert.Equal("existing-desert-map", await File.ReadAllTextAsync(existingMapFile));

        // 2. Loose Desert (1).map gets its own dedicated folder "Desert (1)" with only its map.
        var desert1Dir = Path.Combine(_stagingDirectory, "Desert (1)");
        Assert.True(Directory.Exists(desert1Dir));
        var desert1TargetMap = Path.Combine(desert1Dir, "Desert (1).map");
        Assert.True(File.Exists(desert1TargetMap));
        Assert.Equal("loose-desert-1-map", await File.ReadAllTextAsync(desert1TargetMap));

        // 3. Disambiguated Desert.map skips "Desert (1)" to avoid collision and is placed in "Desert (2)".
        var desert2Dir = Path.Combine(_stagingDirectory, "Desert (2)");
        Assert.True(Directory.Exists(desert2Dir));
        var desert2TargetMap = Path.Combine(desert2Dir, "Desert.map");
        Assert.True(File.Exists(desert2TargetMap));
        Assert.Equal("loose-conflicting-desert-map", await File.ReadAllTextAsync(desert2TargetMap));

        // Ensure each folder only has its own single map file (no multiple map files sharing a directory).
        Assert.Single(Directory.GetFiles(existingDesertDir, "*.map"));
        Assert.Single(Directory.GetFiles(desert1Dir, "*.map"));
        Assert.Single(Directory.GetFiles(desert2Dir, "*.map"));
    }

    /// <summary>
    /// Verifies that in a multi-map payload, a root generic map.ini is not copied to any map directory.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_MultiMapPayloadWithGenericMapIni_DoesNotCopyGenericIniToMapFoldersAsync()
    {
        // Arrange: Desert.map, Snow.map, generic map.ini, and generic map.tga
        Directory.CreateDirectory(_stagingDirectory);
        var desertMap = Path.Combine(_stagingDirectory, "Desert.map");
        var snowMap = Path.Combine(_stagingDirectory, "Snow.map");
        var genericIni = Path.Combine(_stagingDirectory, "map.ini");
        var genericTga = Path.Combine(_stagingDirectory, "map.tga");

        await File.WriteAllTextAsync(desertMap, "desert-map-data");
        await File.WriteAllTextAsync(snowMap, "snow-map-data");
        await File.WriteAllTextAsync(genericIni, "WaterTransparency = 50%");
        await File.WriteAllTextAsync(genericTga, "thumbnail-data");

        // Act
        var processor = CreateProcessor();
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.Map, GameType.ZeroHour);

        // Assert
        var desertFolder = Path.Combine(_stagingDirectory, "Desert");
        var snowFolder = Path.Combine(_stagingDirectory, "Snow");

        Assert.True(Directory.Exists(desertFolder));
        Assert.True(Directory.Exists(snowFolder));

        // Generic map.tga should still be copied/shared to each map folder
        Assert.True(File.Exists(Path.Combine(desertFolder, "Desert.tga")));
        Assert.True(File.Exists(Path.Combine(snowFolder, "Snow.tga")));

        // Generic map.ini must NOT be copied into either map folder for multi-map payloads
        Assert.False(File.Exists(Path.Combine(desertFolder, "map.ini")));
        Assert.False(File.Exists(Path.Combine(snowFolder, "map.ini")));
        Assert.False(File.Exists(Path.Combine(desertFolder, "Desert.ini")));
        Assert.False(File.Exists(Path.Combine(snowFolder, "Snow.ini")));
    }

    /// <summary>
    /// Verifies that in a single-map payload, a root generic map.ini is copied to the map folder.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeDirectoryStructureAsync_SingleMapPayloadWithGenericMapIni_CopiesGenericIniToMapFolderAsync()
    {
        // Arrange: Only Desert.map and generic map.ini
        Directory.CreateDirectory(_stagingDirectory);
        var desertMap = Path.Combine(_stagingDirectory, "Desert.map");
        var genericIni = Path.Combine(_stagingDirectory, "map.ini");

        await File.WriteAllTextAsync(desertMap, "desert-map-data");
        await File.WriteAllTextAsync(genericIni, "WaterTransparency = 50%");

        // Act
        var processor = CreateProcessor();
        await processor.NormalizeDirectoryStructureAsync(_stagingDirectory, ContentType.Map, GameType.ZeroHour);

        // Assert
        var desertFolder = Path.Combine(_stagingDirectory, "Desert");
        Assert.True(Directory.Exists(desertFolder));
        Assert.True(File.Exists(Path.Combine(desertFolder, "map.ini")));
        Assert.Equal("WaterTransparency = 50%", await File.ReadAllTextAsync(Path.Combine(desertFolder, "map.ini")));
    }

    /// <inheritdoc/>
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
                using (var bz = SharpCompress.Compressors.BZip2.BZip2Stream.Create(bzMs, SharpCompress.Compressors.CompressionMode.Compress, decompressConcatenated: false, leaveOpen: false))
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
                using (var def = new DeflateStream(defMs, CompressionLevel.Optimal, leaveOpen: false))
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

        var compressedTable = Array.Empty<byte>();
        using (var defTableMs = new MemoryStream())
        {
            defTableMs.WriteByte(0x78);
            defTableMs.WriteByte(0xDA);
            using (var def = new DeflateStream(defTableMs, CompressionLevel.Optimal, leaveOpen: false))
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
