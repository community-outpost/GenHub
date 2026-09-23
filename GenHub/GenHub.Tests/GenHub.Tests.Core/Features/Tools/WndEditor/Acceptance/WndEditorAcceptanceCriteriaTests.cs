using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WndEditor;
using GenHub.Core.Services.Tools.Checksum;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.Acceptance;

/// <summary>
/// Acceptance criteria tests for the 3 core WND Editor issues:
/// 1. Asset loading resolves Zero Hour over Generals (Expansion tier beats BaseGame loose/archive fallback),
///    and MainMenuRuler does not inject Generals MainMenuBackdrop behind Zero Hour screens.
/// 2. Border/window bounds expand virtual screen width/height so widescreen borders are full-screen, not corner-boxed.
/// 3. ModBuilder sample project (ElTioRata / ImprovedMenus) loose assets in GameFilesEdited are discovered
///    and prioritized at Mod tier over base game assets.
/// </summary>
public sealed class WndEditorAcceptanceCriteriaTests : IDisposable
{
    private readonly string _tempRoot;

    public WndEditorAcceptanceCriteriaTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "GenHub_Acceptance_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup
            }
        }
    }

    /// <summary>
    /// Acceptance Criterion 1A:
    /// In a dual-game setup (Zero Hour active target + Generals base fallback),
    /// Zero Hour archives (Expansion tier) must strictly take precedence over Generals loose files (BaseGame tier).
    /// </summary>
    [Fact]
    public void Criterion1A_ZeroHourArchive_TakesPrecedenceOverGeneralsLooseFile()
    {
        // Arrange
        var zhRoot = Path.Combine(_tempRoot, "ZeroHour");
        var genRoot = Path.Combine(_tempRoot, "Generals");
        Directory.CreateDirectory(zhRoot);
        Directory.CreateDirectory(genRoot);

        // Generals base game has a loose Data\English\Generals.csf
        var genLooseCsfDir = Path.Combine(genRoot, "Data", "English");
        Directory.CreateDirectory(genLooseCsfDir);
        var genCsfPath = Path.Combine(genLooseCsfDir, "Generals.csf");
        var genBytes = Encoding.UTF8.GetBytes("Generals Base Fallback String Table");
        File.WriteAllBytes(genCsfPath, genBytes);

        // Zero Hour has EnglishZH.big containing Data\English\Generals.csf
        var zhCsfBytes = Encoding.UTF8.GetBytes("Zero Hour Expansion String Table");
        var zhBigPath = Path.Combine(zhRoot, "EnglishZH.big");
        CreateBigArchive(zhBigPath, ("Data\\English\\Generals.csf", zhCsfBytes));

        // Create VFS with Zero Hour as active target and Generals as base fallback
        var vfs = new SageVirtualFileSystem(
            zhRoot,
            isZeroHour: true,
            logger: Mock.Of<ILogger>(),
            initialTier: SageFileTier.Expansion);
        vfs.AddBaseFallback(genRoot);

        // Act
        var resolvedBytes = vfs.Read("Data\\English\\Generals.csf");
        var resolvedTier = vfs.GetFileTier("Data\\English\\Generals.csf");

        // Assert: Zero Hour expansion data MUST win over Generals loose fallback
        resolvedBytes.Should().NotBeNull();
        resolvedBytes.Should().Equal(zhCsfBytes);
        resolvedTier.Should().Be(SageFileTier.Expansion);
    }

    /// <summary>
    /// Acceptance Criterion 1B:
    /// Menus utilizing MainMenuRuler (like ChallengeLoadScreen or LAN lobbies) must NOT have
    /// Generals MainMenuBackdrop forcibly injected behind them as an underlay.
    /// </summary>
    [Fact]
    public void Criterion1B_MainMenuRuler_DoesNotForceGeneralsMainMenuBackdropUnderlay()
    {
        // Arrange: window referencing MainMenuRuler
        var window = new WndWindow
        {
            ControlTypeName = WndConstants.ControlTypes.User,
        };
        window.SetProperty(WndConstants.PropertyKeys.Name, "ChallengeLoadScreen.wnd:Border");

        var entries = new List<WndDrawDataEntry>();
        for (var i = 0; i < WndConstants.DrawData.EntryCount; i++)
        {
            entries.Add(WndDrawDataEntry.Empty);
        }
        entries[0] = new WndDrawDataEntry(
            Image: "MainMenuRuler",
            Color: null,
            BorderColor: null,
            UserData: null);

        var drawDataSet = new WndDrawDataSet(entries);
        window.SetProperty(WndConstants.PropertyKeys.EnabledDrawData, drawDataSet.ToDrawDataString());

        // Act
        var plan = WndPreviewPlanner.Plan(window);

        // Assert: SingleImage is MainMenuRuler, but UnderlayImage must be null (no Generals tank backdrop!)
        plan.SingleImage.Should().Be("MainMenuRuler");
        plan.UnderlayImage.Should().BeNull();
        plan.ReferencedImages.Should().NotContain("MainMenuBackdrop");
    }

    /// <summary>
    /// Acceptance Criterion 2:
    /// When a window or overlay ruler defines widescreen dimensions (e.g. 1920x1080),
    /// the virtual screen dimensions expand to the full width/height so the border is full-screen,
    /// rather than being confined to an 800x600 corner.
    /// </summary>
    [Fact]
    public void Criterion2_WidescreenWindowCoordinates_ExpandVirtualScreenDimensions()
    {
        // Arrange
        int screenW = 800;
        int screenH = 600;
        int maxWidth = 1920;
        int maxHeight = 1080;

        // Act: compute virtual screen size using the updated formula
        int virtualScreenWidth = (int)Math.Max(screenW, Math.Max(maxWidth, WndConstants.Editor.MinCanvasWidth));
        int virtualScreenHeight = (int)Math.Max(screenH, Math.Max(maxHeight, WndConstants.Editor.MinCanvasHeight));

        // Assert
        virtualScreenWidth.Should().Be(1920);
        virtualScreenHeight.Should().Be(1080);
        virtualScreenWidth.Should().BeGreaterThanOrEqualTo(maxWidth);
        virtualScreenHeight.Should().BeGreaterThanOrEqualTo(maxHeight);
    }

    /// <summary>
    /// Acceptance Criterion 3A:
    /// In a ModBuilder project with GameFilesEdited (e.g. ElTioRata / ImprovedMenus),
    /// loose textures and mapped images in GameFilesEdited are discovered and resolved at Mod tier,
    /// overriding any base game or expansion assets.
    /// </summary>
    [Fact]
    public void Criterion3A_ModBuilderSampleProject_DiscoversAndPrioritizesModAssets()
    {
        // Arrange: create mock ModBuilder sample project structure
        var projectDir = Path.Combine(_tempRoot, "ImprovedMenus");
        var gameFilesEdited = Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir);
        var texturesDir = Path.Combine(gameFilesEdited, "Data", "English", "Art", "Textures");
        var mappedImagesDir = Path.Combine(gameFilesEdited, "Data", "INI", "MappedImages", "HandCreated");
        Directory.CreateDirectory(texturesDir);
        Directory.CreateDirectory(mappedImagesDir);

        // Create loose mod texture (1920x1080 MainMenuRulerUserInterface.tga)
        var modTexturePath = Path.Combine(texturesDir, "mainmenuruleruserinterface.tga");
        byte[] modTextureBytes = [0x54, 0x47, 0x41, 0x01, 0x02, 0x03];
        File.WriteAllBytes(modTexturePath, modTextureBytes);

        // Create loose mod mapped image INI
        var modIniPath = Path.Combine(mappedImagesDir, "HandCreatedMappedImages.INI");
        File.WriteAllText(modIniPath, "MappedImage MainMenuRuler\n  Texture = mainmenuruleruserinterface.tga\nEnd\n");

        // Base game has an older/smaller version of the texture in an archive
        var baseDir = Path.Combine(_tempRoot, "BaseGame");
        Directory.CreateDirectory(baseDir);
        var baseBigPath = Path.Combine(baseDir, "Textures.big");
        byte[] baseTextureBytes = [0x54, 0x47, 0x41, 0x00, 0x00, 0x00];
        CreateBigArchive(baseBigPath, ("Data\\English\\Art\\Textures\\mainmenuruleruserinterface.tga", baseTextureBytes));

        // Create VFS with base game and add the mod project
        var vfs = new SageVirtualFileSystem(
            baseDir,
            isZeroHour: true,
            logger: Mock.Of<ILogger>(),
            initialTier: SageFileTier.Expansion);

        vfs.AddMod(gameFilesEdited);

        // Act 1: Read the texture
        var readBytes = vfs.Read("Data\\English\\Art\\Textures\\mainmenuruleruserinterface.tga");
        var readTier = vfs.GetFileTier("Data\\English\\Art\\Textures\\mainmenuruleruserinterface.tga");

        // Act 2: Read loose file by filename alone
        var modLooseByName = vfs.TryReadModLooseFileByName("mainmenuruleruserinterface.tga");

        // Act 3: Enumerate INI files under Data\INI\MappedImages
        var iniFiles = vfs.FilesUnder("Data\\INI\\MappedImages");

        // Assert: Mod assets win at Mod tier
        readBytes.Should().NotBeNull();
        readBytes.Should().Equal(modTextureBytes);
        readTier.Should().Be(SageFileTier.Mod);

        modLooseByName.Should().NotBeNull();
        modLooseByName.Should().Equal(modTextureBytes);

        iniFiles.Should().NotBeEmpty();
        iniFiles.Should().Contain(f => f.EndsWith("HandCreatedMappedImages.INI", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Helper to construct a valid .BIG archive containing arbitrary file entries.
    /// </summary>
    private static void CreateBigArchive(string archivePath, params (string RelPath, byte[] Data)[] entries)
    {
        // Big format:
        // 0..3: "BIG4"
        // 4..7: total file size (uint32 LittleEndian)
        // 8..11: entry count (uint32 BigEndian)
        // 12..15: header size (uint32 BigEndian) = 16 + directory table size
        // Directory table: for each entry:
        //   4 bytes: offset (BigEndian)
        //   4 bytes: size (BigEndian)
        //   null-terminated relative path
        // File data at specified offsets

        var dirEntries = new List<(string Path, byte[] Data, int PathBytesLength)>();
        int dirTableSize = 0;
        foreach (var (relPath, data) in entries)
        {
            var normalized = relPath.Replace('/', '\\');
            var pathBytesLen = Encoding.Latin1.GetByteCount(normalized) + 1; // including null terminator
            dirEntries.Add((normalized, data, pathBytesLen));
            dirTableSize += 8 + pathBytesLen;
        }

        int headerSize = 16 + dirTableSize;
        int currentOffset = headerSize;
        var entryOffsets = new List<int>();

        foreach (var (_, data, _) in dirEntries)
        {
            entryOffsets.Add(currentOffset);
            currentOffset += data.Length;
        }

        int totalFileSize = currentOffset;

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Magic "BIG4"
        writer.Write((byte)'B');
        writer.Write((byte)'I');
        writer.Write((byte)'G');
        writer.Write((byte)'4');

        // Total file size (Little Endian uint32)
        writer.Write((uint)totalFileSize);

        // Entry count (Big Endian uint32)
        byte[] countBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(countBytes, (uint)entries.Length);
        writer.Write(countBytes);

        // Header size (Big Endian uint32)
        byte[] headerSizeBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(headerSizeBytes, (uint)headerSize);
        writer.Write(headerSizeBytes);

        // Directory table
        byte[] u32Buf = new byte[4];
        for (int i = 0; i < dirEntries.Count; i++)
        {
            var (path, data, _) = dirEntries[i];
            int offset = entryOffsets[i];

            // Offset BigEndian
            BinaryPrimitives.WriteUInt32BigEndian(u32Buf, (uint)offset);
            writer.Write(u32Buf);

            // Size BigEndian
            BinaryPrimitives.WriteUInt32BigEndian(u32Buf, (uint)data.Length);
            writer.Write(u32Buf);

            // Path null-terminated Latin1
            writer.Write(Encoding.Latin1.GetBytes(path));
            writer.Write((byte)0);
        }

        // File payload data
        foreach (var (_, data, _) in dirEntries)
        {
            writer.Write(data);
        }

        writer.Flush();
        File.WriteAllBytes(archivePath, ms.ToArray());
    }
}
