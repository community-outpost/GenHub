using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.Checksum;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Services.Tools.Checksum;

/// <summary>
/// Service implementing native SAGE engine CRC calculations for game executables and INI data hierarchies.
/// </summary>
public sealed class GameCrcCalculatorService : IGameCrcCalculatorService
{
    private readonly ConcurrentDictionary<string, (DateTime LastWriteTimeUtc, long FileLength, string Crc)> _exeCrcCache = new(StringComparer.OrdinalIgnoreCase);

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

        try
        {
            var fileInfo = new FileInfo(executablePath);
            var cacheKey = $"{fileInfo.FullName}|{gameRootPath}|{major}|{minor}";
            if (_exeCrcCache.TryGetValue(cacheKey, out var cached) &&
                cached.LastWriteTimeUtc == fileInfo.LastWriteTimeUtc &&
                cached.FileLength == fileInfo.Length)
            {
                return OperationResult<string>.CreateSuccess(cached.Crc);
            }

            return await Task.Run(
                () =>
                {
                    ct.ThrowIfCancellationRequested();

                    var readResult = ReadExecutableBytes(executablePath);
                    if (!readResult.Success || readResult.Data == null)
                    {
                        var errorMessage = readResult.Errors.Count > 0 ? readResult.Errors[0] : "Failed to read executable.";
                        return OperationResult<string>.CreateFailure(errorMessage);
                    }

                    var exeBytes = readResult.Data;

                    var crc = new LegacyChecksum();
                    crc.Add(exeBytes);

                    var (resolvedMajor, resolvedMinor) = ResolveVersion(exeBytes, executablePath, major, minor);
                    AddVersionBytes(crc, resolvedMajor, resolvedMinor);

                    string root = gameRootPath ?? Path.GetDirectoryName(executablePath) ?? string.Empty;
                    AddScriptFiles(crc, root);

                    var calculatedCrc = $"0x{crc.Value:X8}";
                    _exeCrcCache[cacheKey] = (fileInfo.LastWriteTimeUtc, fileInfo.Length, calculatedCrc);
                    return OperationResult<string>.CreateSuccess(calculatedCrc);
                },
                ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperationResult<string>.CreateFailure($"Failed to calculate executable CRC: {ex.Message}");
        }
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
                var vfs = new SageVirtualFileSystem(gameRootPath, isZeroHour, cancellationToken: ct);
                var crc = new XferChecksum();

                var order = isZeroHour
                    ? SageChecksumConstants.GeneralsMdOrder
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

    private static OperationResult<byte[]> ReadExecutableBytes(string executablePath)
    {
        try
        {
            return OperationResult<byte[]>.CreateSuccess(File.ReadAllBytes(executablePath));
        }
        catch (IOException ex)
        {
            return OperationResult<byte[]>.CreateFailure($"Failed to read executable: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult<byte[]>.CreateFailure($"Failed to read executable: {ex.Message}");
        }
    }

    private static (int Major, int Minor) ResolveVersion(byte[] exeBytes, string executablePath, int? major, int? minor)
    {
        int detectedMajor = 0;
        int detectedMinor = 0;

        if (PeVersionExtractor.TryExtract(exeBytes, out int extractedMajor, out int extractedMinor) ||
            PeVersionExtractor.TryExtractFromVersionInfo(executablePath, out extractedMajor, out extractedMinor))
        {
            detectedMajor = extractedMajor;
            detectedMinor = extractedMinor;
        }
        else
        {
            // Fallback based on filename convention
            string name = Path.GetFileName(executablePath).ToLowerInvariant();
            if (name.Contains("zh") || name.Contains("zerohour"))
            {
                detectedMajor = 1;
                detectedMinor = 4;
            }
            else
            {
                detectedMajor = 1;
                detectedMinor = 8;
            }
        }

        return (major ?? detectedMajor, minor ?? detectedMinor);
    }

    private static void AddVersionBytes(LegacyChecksum crc, int major, int minor)
    {
        byte[] versionBytes =
        [
            (byte)(minor & 0xFF),
            (byte)((minor >> 8) & 0xFF),
            (byte)((minor >> 16) & 0xFF),
            (byte)((minor >> 24) & 0xFF),
            (byte)(major & 0xFF),
            (byte)((major >> 8) & 0xFF),
            (byte)((major >> 16) & 0xFF),
            (byte)((major >> 24) & 0xFF),
        ];

        crc.Add(versionBytes);
    }

    private static void AddScriptFiles(LegacyChecksum crc, string root)
    {
        if (string.IsNullOrEmpty(root))
        {
            return;
        }

        string skirmishPath = Path.Combine(root, SageChecksumConstants.SkirmishScriptsRelativePath);
        if (File.Exists(skirmishPath))
        {
            TryAddFileBytes(crc, skirmishPath);
        }

        string mpPath = Path.Combine(root, SageChecksumConstants.MultiplayerScriptsRelativePath);
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
        catch (IOException)
        {
            // Ignore script read failure per specific exception convention
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore script read failure per specific exception convention
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
            foreach (var side in sideloadPaths)
            {
                vfs.AddSideload(side);
            }
        }

        if (!string.IsNullOrEmpty(modPath))
        {
            vfs.AddMod(modPath);
        }
    }

    private static (string DefaultPath, string OverridePath)[] BuildGeneralsOrder()
    {
        // Vanilla Generals excludes Weather (indices 3 and 4 in GeneralsMdOrder)
        return
        [
            .. SageChecksumConstants.GeneralsMdOrder[..3],
            .. SageChecksumConstants.GeneralsMdOrder[5..],
        ];
    }

    private static void LoadDirectory(SageVirtualFileSystem vfs, string path, XferChecksum crc)
    {
        void Read(string file)
        {
            byte[]? data = vfs.Read(file);
            if (data != null)
            {
                IniNormalizer.ProcessLines(data, crc.Add);
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
