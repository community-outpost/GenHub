using GenHub.Core.Interfaces.Tools.Checksum;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Services.Tools.Checksum;

/// <summary>
/// Service implementing SAGE engine executable and configuration checksum calculations.
/// </summary>
public sealed class GameCrcCalculatorService : IGameCrcCalculatorService
{
    private static readonly (string DefaultPath, string OverridePath)[] GeneralsMdOrder =
    [
        (@"Data\INI\Default\GameData", @"Data\INI\GameData"),
        (@"Data\INI\Default\Water", string.Empty),
        (@"Data\INI\Water", string.Empty),
        (@"Data\INI\Default\Weather", string.Empty),
        (@"Data\INI\Weather", string.Empty),
        (@"Data\INI\Default\Science", @"Data\INI\Science"),
        (@"Data\INI\Default\Multiplayer", @"Data\INI\Multiplayer"),
        (@"Data\INI\Default\Terrain", @"Data\INI\Terrain"),
        (@"Data\INI\Default\Roads", @"Data\INI\Roads"),
        (string.Empty, @"Data\INI\Rank"),
        (@"Data\INI\Default\PlayerTemplate", @"Data\INI\PlayerTemplate"),
        (@"Data\INI\Default\FXList", @"Data\INI\FXList"),
        (string.Empty, @"Data\INI\Weapon"),
        (@"Data\INI\Default\ObjectCreationList", @"Data\INI\ObjectCreationList"),
        (string.Empty, @"Data\INI\Locomotor"),
        (@"Data\INI\Default\SpecialPower", @"Data\INI\SpecialPower"),
        (string.Empty, @"Data\INI\DamageFX"),
        (string.Empty, @"Data\INI\Armor"),
        (@"Data\INI\Default\Object", @"Data\INI\Object"),
        (@"Data\INI\Default\Upgrade", @"Data\INI\Upgrade"),
        (@"Data\INI\Default\AIData", @"Data\INI\AIData"),
        (@"Data\INI\Default\Crate", @"Data\INI\Crate"),
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

                int resolvedMajor = major ?? 0;
                int resolvedMinor = minor ?? 0;

                if (major == null || minor == null)
                {
                    if (PeVersionExtractor.TryExtract(exeBytes, out int extractedMajor, out int extractedMinor))
                    {
                        resolvedMajor = extractedMajor;
                        resolvedMinor = extractedMinor;
                    }
                    else if (PeVersionExtractor.TryExtractFromFile(executablePath, out extractedMajor, out extractedMinor))
                    {
                        resolvedMajor = extractedMajor;
                        resolvedMinor = extractedMinor;
                    }
                    else
                    {
                        // Fallback based on filename convention
                        string name = Path.GetFileName(executablePath).ToLowerInvariant();
                        if (name.Contains("zh") || name.Contains("zerohour"))
                        {
                            resolvedMajor = 1;
                            resolvedMinor = 4;
                        }
                        else
                        {
                            resolvedMajor = 1;
                            resolvedMinor = 8;
                        }
                    }
                }

                byte[] versionBytes =
                [
                    (byte)(resolvedMinor & 0xFF),
                    (byte)((resolvedMinor >> 8) & 0xFF),
                    (byte)(resolvedMajor & 0xFF),
                    (byte)((resolvedMajor >> 8) & 0xFF)
                ];
                crc.Add(versionBytes);

                string root = gameRootPath ?? Path.GetDirectoryName(executablePath) ?? string.Empty;
                if (!string.IsNullOrEmpty(root))
                {
                    string skirmishPath = Path.Combine(root, "Data", "Scripts", "SkirmishScripts.scb");
                    if (File.Exists(skirmishPath))
                    {
                        try
                        {
                            crc.Add(File.ReadAllBytes(skirmishPath));
                        }
                        catch
                        {
                            // Ignore script read failure
                        }
                    }

                    string mpPath = Path.Combine(root, "Data", "Scripts", "MultiplayerScripts.scb");
                    if (File.Exists(mpPath))
                    {
                        try
                        {
                            crc.Add(File.ReadAllBytes(mpPath));
                        }
                        catch
                        {
                            // Ignore script read failure
                        }
                    }
                }

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
                if (!string.IsNullOrEmpty(order[0].DefaultPath))
                {
                    LoadDirectory(vfs, order[0].DefaultPath, crc);
                }

                if (!string.IsNullOrEmpty(order[0].OverridePath))
                {
                    LoadDirectory(vfs, order[0].OverridePath, crc);
                }

                // Phase 2: Mount Sideloads and Mods
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

                // Phase 3: Load remaining categories (Water, Weather, Science, Objects, etc.)
                for (int i = 1; i < order.Length; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    var (defaultPath, overridePath) = order[i];
                    if (!string.IsNullOrEmpty(defaultPath))
                    {
                        LoadDirectory(vfs, defaultPath, crc);
                    }

                    if (!string.IsNullOrEmpty(overridePath))
                    {
                        LoadDirectory(vfs, overridePath, crc);
                    }
                }

                return OperationResult<string>.CreateSuccess($"0x{crc.Value:X8}");
            },
            ct);
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

        return list.ToArray();
    }

    private static void LoadDirectory(SageVirtualFileSystem vfs, string path, XferChecksum crc)
    {
        bool Read(string file)
        {
            byte[]? data = vfs.Read(file);
            if (data == null)
            {
                return false;
            }

            IniNormalizer.ProcessLines(data, line => crc.Add(line));
            return true;
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
