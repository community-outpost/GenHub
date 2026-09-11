using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Services.Tools.Checksum;

/// <summary>
/// Service implementing native SAGE engine CRC calculations for game executables and INI data hierarchies.
/// </summary>
public sealed class GameCrcCalculatorService : IGameCrcCalculatorService
{
    private static readonly (string DefaultPath, string OverridePath)[] GeneralsMdOrder =
    [
        (@\"Data\INI\Default\GameData\", @\"Data\INI\GameData\"),
        (string.Empty, @\"Data\INI\GameData.ini\"),
        (string.Empty, @\"Data\INI\Default\GameData.ini\"),
        (string.Empty, @\"Data\INI\INIZH.ini\"),
        (string.Empty, @\"Data\INI\Default\INIZH.ini\"),
        (@\"Data\INI\Default\Water\", @\"Data\INI\Water\"),
        (string.Empty, @\"Data\INI\Default\Weather.ini\"),
        (string.Empty, @\"Data\INI\Weather.ini\"),
        (string.Empty, @\"Data\INI\Default\Terrain.ini\"),
        (string.Empty, @\"Data\INI\Terrain.ini\"),
        (string.Empty, @\"Data\INI\Default\Road.ini\"),
        (string.Empty, @\"Data\INI\Road.ini\"),
        (string.Empty, @\"Data\INI\Default\Handicap.ini\"),
        (string.Empty, @\"Data\INI\Handicap.ini\"),
        (string.Empty, @\"Data\INI\Default\CommandSet.ini\"),
        (string.Empty, @\"Data\INI\CommandSet.ini\"),
        (string.Empty, @\"Data\INI\Default\CommandButton.ini\"),
        (string.Empty, @\"Data\INI\CommandButton.ini\"),
        (string.Empty, @\"Data\INI\Default\Science.ini\"),
        (string.Empty, @\"Data\INI\Science.ini\"),
        (string.Empty, @\"Data\INI\Default\ModifierList.ini\"),
        (string.Empty, @\"Data\INI\ModifierList.ini\"),
        (string.Empty, @\"Data\INI\Default\ControlBarScheme.ini\"),
        (string.Empty, @\"Data\INI\ControlBarScheme.ini\"),
        (string.Empty, @\"Data\INI\Default\Video.ini\"),
        (string.Empty, @\"Data\INI\Video.ini\"),
        (string.Empty, @\"Data\INI\Default\AudioFX.ini\"),
        (string.Empty, @\"Data\INI\AudioFX.ini\"),
        (string.Empty, @\"Data\INI\Default\Animation.ini\"),
        (string.Empty, @\"Data\INI\Animation.ini\"),
        (string.Empty, @\"Data\INI\Default\Rank.ini\"),
        (string.Empty, @\"Data\INI\Rank.ini\"),
        (string.Empty, @\"Data\INI\Default\WebBanners.ini\"),
        (string.Empty, @\"Data\INI\WebBanners.ini\"),
        (string.Empty, @\"Data\INI\Default\MiscFX.ini\"),
        (string.Empty, @\"Data\INI\MiscFX.ini\"),
        (string.Empty, @\"Data\INI\Default\ParticleSystem.ini\"),
        (string.Empty, @\"Data\INI\ParticleSystem.ini\"),
        (string.Empty, @\"Data\INI\Default\FXList.ini\"),
        (string.Empty, @\"Data\INI\FXList.ini\"),
        (string.Empty, @\"Data\INI\Default\DamageFX.ini\"),
        (string.Empty, @\"Data\INI\DamageFX.ini\"),
        (string.Empty, @\"Data\INI\Default\Armor.ini\"),
        (string.Empty, @\"Data\INI\Armor.ini\"),
        (string.Empty, @\"Data\INI\Default\Locomotor.ini\"),
        (string.Empty, @\"Data\INI\Locomotor.ini\"),
        (string.Empty, @\"Data\INI\Default\SpecialPower.ini\"),
        (string.Empty, @\"Data\INI\SpecialPower.ini\"),
        (string.Empty, @\"Data\INI\Default\Weapon.ini\"),
        (string.Empty, @\"Data\INI\Weapon.ini\"),
        (string.Empty, @\"Data\INI\DamageFX\"),
        (string.Empty, @\"Data\INI\Armor\"),
        (@\"Data\INI\Default\Object\", @\"Data\INI\Object\"),
        (@\"Data\INI\Default\Upgrade\", @\"Data\INI\Upgrade\"),
        (@\"Data\INI\Default\AIData\", @\"Data\INI\AIData\"),
        (@\"Data\INI\Default\Crate\", @\"Data\INI\Crate\"),
    ];

    /// <inheritdoc/>
    public async Task<OperationResult<string>> CalculateExeCrcAsync(
        string executablePath,
        string? gameRootPath = null,
        int? major = null,
        int? minor = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return OperationResult<string>.CreateFailure($"Executable not found at '{executablePath}'.");
        }

        return await Task.Run(
            () =>
            {
                ct.ThrowIfCancellationRequested();

                byte[] exeBytes;
                try
                {
                    exeBytes = File.ReadAllBytes(executablePath);
                }
                catch (Exception ex)
                {
                    return OperationResult<string>.CreateFailure($"Failed to read executable: {ex.Message}");
                }

                var crc = new LegacyChecksum();
                crc.Add(exeBytes);

                var (resolvedMajor, resolvedMinor) = ResolveVersion(exeBytes, executablePath, major, minor);
                AddVersionBytes(crc, resolvedMajor, resolvedMinor);

                string root = gameRootPath ?? Path.GetDirectoryName(executablePath) ?? string.Empty;
                AddScriptFiles(crc, root);

                return OperationResult<string>.CreateSuccess($"0x{crc.Value:X8}");
            },
            ct);
    }

    /// <inheritdoc/>
    public async Task<OperationResult<string>> CalculateIniCrcAsync(
        string gameRootPath,
        GameType gameType,
        IReadOnlyList<string>? sideloadPaths = null,
        string? modPath = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(gameRootPath) || !Directory.Exists(gameRootPath))
        {
            return OperationResult<string>.CreateFailure($"Game root directory not found at '{gameRootPath}'.");
        }

        return await Task.Run(
            () =>
            {
                ct.ThrowIfCancellationRequested();

                bool isZeroHour = gameType == GameType.ZeroHour;
                var vfs = new SageVirtualFileSystem(gameRootPath, isZeroHour);
                var crc = new XferChecksum();

                var order = isZeroHour
                    ? GeneralsMdOrder
                    : BuildGeneralsOrder();

                // Phase 1: Load GameData before sideloads/mods are mounted
                LoadOrderStep(order[0], vfs, crc);

                // Phase 2: Mount Sideloads and Mods
                MountSideloadsAndMods(vfs, sideloadPaths, modPath);

                // Phase 3: Load remaining categories
                for (int i = 1; i < order.Length; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    LoadOrderStep(order[i], vfs, crc);
                }

                return OperationResult<string>.CreateSuccess($"0x{crc.Value:X8}");
            },
            ct);
    }

    private static (int Major, int Minor) ResolveVersion(byte[] exeBytes, string executablePath, int? major, int? minor)
    {
        if (major != null && minor != null)
        {
            return (major.Value, minor.Value);
        }

        if (PeVersionExtractor.TryExtract(exeBytes, out int extractedMajor, out int extractedMinor) ||
            PeVersionExtractor.TryExtractFromFile(executablePath, out extractedMajor, out extractedMinor))
        {
            return (extractedMajor, extractedMinor);
        }

        // Fallback based on filename convention
        string name = Path.GetFileName(executablePath).ToLowerInvariant();
        if (name.Contains("zh") || name.Contains("zerohour"))
        {
            return (1, 4);
        }

        return (1, 8);
    }

    private static void AddVersionBytes(LegacyChecksum crc, int major, int minor)
    {
        byte[] versionBytes =
        [
            (byte)(minor & 0xFF),
            (byte)((minor >> 8) & 0xFF),
            (byte)(major & 0xFF),
            (byte)((major >> 8) & 0xFF)
        ];
        crc.Add(versionBytes);
    }

    private static void AddScriptFiles(LegacyChecksum crc, string root)
    {
        if (string.IsNullOrEmpty(root))
        {
            return;
        }

        string skirmishPath = Path.Combine(root, "Data", "Scripts", "SkirmishScripts.scb");
        if (File.Exists(skirmishPath))
        {
            TryAddFileBytes(crc, skirmishPath);
        }

        string mpPath = Path.Combine(root, "Data", "Scripts", "MultiplayerScripts.scb");
        if (File.Exists(mpPath))
        {
            TryAddFileBytes(crc, mpPath);
        }
    }

    private static void TryAddFileBytes(LegacyChecksum crc, string path)
    {
        try
        {
            crc.Add(File.ReadAllBytes(path));
        }
        catch
        {
            // Ignore script read failure
        }
    }

    private static void LoadOrderStep((string DefaultPath, string OverridePath) step, SageVirtualFileSystem vfs, XferChecksum crc)
    {
        if (!string.IsNullOrEmpty(step.DefaultPath))
        {
            LoadDirectory(vfs, step.DefaultPath, crc);
        }

        if (!string.IsNullOrEmpty(step.OverridePath))
        {
            LoadDirectory(vfs, step.OverridePath, crc);
        }
    }

    private static void MountSideloadsAndMods(SageVirtualFileSystem vfs, IReadOnlyList<string>? sideloadPaths, string? modPath)
    {
        if (sideloadPaths != null)
        {
            foreach (string sideload in sideloadPaths)
            {
                vfs.AddSideload(sideload);
            }
        }

        if (!string.IsNullOrWhiteSpace(modPath))
        {
            vfs.AddMod(modPath);
        }
    }

    private static (string DefaultPath, string OverridePath)[] BuildGeneralsOrder()
    {
        var list = new List<(string DefaultPath, string OverridePath)>();
        for (int i = 0; i < 3; i++)
        {
            list.Add(GeneralsMdOrder[i]);
        }

        for (int i = 5; i < GeneralsMdOrder.Length; i++)
        {
            list.Add(GeneralsMdOrder[i]);
        }

        return [.. list];
    }

    private static void LoadDirectory(SageVirtualFileSystem vfs, string path, XferChecksum crc)
    {
        void Read(string file)
        {
            byte[]? data = vfs.Read(file);
            if (data != null)
            {
                IniNormalizer.ProcessLines(data, line => crc.Add(line));
            }
        }

        Read(path + ".ini");

        var files = vfs.FilesUnder(path);
        if (files.Count == 0)
        {
            return;
        }

        int prefixLen = path.Length + 1;

        // Non-nested files first
        foreach (string file in files)
        {
            if (file.Length > prefixLen && !file[prefixLen..].Contains('\\'))
            {
                Read(file);
            }
        }

        // Nested files second
        foreach (string file in files)
        {
            if (file.Length > prefixLen && file[prefixLen..].Contains('\\'))
            {
                Read(file);
            }
        }
    }
}
