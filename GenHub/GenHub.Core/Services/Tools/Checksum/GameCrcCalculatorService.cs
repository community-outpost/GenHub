using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.Checksum;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Services.Tools.Checksum;

/// <summary>
/// Service implementing native SAGE engine CRC calculations for game executables and INI data hierarchies.
/// </summary>
public sealed class GameCrcCalculatorService : IGameCrcCalculatorService
{
    private sealed class FreshnessAccumulator
    {
        public long MaxTicks { get; private set; }

        public long TotalLength { get; private set; }

        public int FileCount { get; private set; }

        public void ObserveTicks(long ticks)
        {
            if (ticks > MaxTicks)
            {
                MaxTicks = ticks;
            }
        }

        public void AddFile(FileInfo file)
        {
            ObserveTicks(file.LastWriteTimeUtc.Ticks);
            TotalLength += file.Length;
            FileCount++;
        }
    }

    private static readonly ConcurrentDictionary<string, (DateTime LastWriteTimeUtc, long FileLength, long SkirmishTicks, long SkirmishLength, long MpTicks, long MpLength, string Crc)> ExeCrcCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, (long MaxTicks, long TotalLength, int FileCount, string Crc)> IniCrcCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<GameCrcCalculatorService>? _logger;

    /// <summary>
    /// Clears both executable and INI CRC caches.
    /// </summary>
    public static void ClearCache()
    {
        ExeCrcCache.Clear();
        IniCrcCache.Clear();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GameCrcCalculatorService"/> class.
    /// </summary>
    /// <param name="logger">Optional logger instance.</param>
    public GameCrcCalculatorService(ILogger<GameCrcCalculatorService>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<OperationResult<string>> CalculateExeCrcAsync(
        string executablePath,
        string? gameRootPath = null,
        int? major = null,
        int? minor = null,
        CancellationToken ct = default)
        => CalculateExeCrcAsync(executablePath, gameRootPath, major, minor, gameType: null, ct);

    /// <inheritdoc/>
    public async Task<OperationResult<string>> CalculateExeCrcAsync(
        string executablePath,
        string? gameRootPath,
        int? major,
        int? minor,
        GameType? gameType,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return OperationResult<string>.CreateFailure($"Executable not found at '{executablePath}'.");
        }

        try
        {
            var fileInfo = new FileInfo(executablePath);
            string root = gameRootPath ?? Path.GetDirectoryName(executablePath) ?? string.Empty;
            var scriptsSig = GetScriptsSignature(root);
            var cacheKey = $"{fileInfo.FullName}|{gameRootPath}|{major}|{minor}|{gameType}";

            if (ExeCrcCache.TryGetValue(cacheKey, out var cached) &&
                cached.LastWriteTimeUtc == fileInfo.LastWriteTimeUtc &&
                cached.FileLength == fileInfo.Length &&
                cached.SkirmishTicks == scriptsSig.SkirmishTicks &&
                cached.SkirmishLength == scriptsSig.SkirmishLength &&
                cached.MpTicks == scriptsSig.MpTicks &&
                cached.MpLength == scriptsSig.MpLength)
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

                    var (resolvedMajor, resolvedMinor) = ResolveVersion(exeBytes, executablePath, major, minor, gameType);
                    AddVersionBytes(crc, resolvedMajor, resolvedMinor);

                    bool scriptsReadSuccessfully = AddScriptFiles(crc, root);
                    if (!scriptsReadSuccessfully)
                    {
                        return OperationResult<string>.CreateFailure($"Failed to read script files for executable CRC calculation in '{root}'.");
                    }

                    var calculatedCrc = $"0x{crc.Value:X8}";
                    ExeCrcCache[cacheKey] = (fileInfo.LastWriteTimeUtc, fileInfo.Length, scriptsSig.SkirmishTicks, scriptsSig.SkirmishLength, scriptsSig.MpTicks, scriptsSig.MpLength, calculatedCrc);

                    return OperationResult<string>.CreateSuccess(calculatedCrc);
                },
                ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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

        var sideloadsPart = sideloadPaths != null && sideloadPaths.Count > 0 ? string.Join(';', sideloadPaths) : string.Empty;
        var cacheKey = $"{gameRootPath}|{gameType}|{sideloadsPart}|{modPath}";
        var freshness = GetIniFreshnessSignature(gameRootPath, sideloadPaths, modPath);

        if (IniCrcCache.TryGetValue(cacheKey, out var cachedIni) &&
            cachedIni.MaxTicks == freshness.MaxTicks &&
            cachedIni.TotalLength == freshness.TotalLength &&
            cachedIni.FileCount == freshness.FileCount)
        {
            return OperationResult<string>.CreateSuccess(cachedIni.Crc);
        }

        try
        {
            return await Task.Run(
                () =>
                {
                    ct.ThrowIfCancellationRequested();

                    bool isZeroHour = gameType == GameType.ZeroHour;
                    var vfs = new SageVirtualFileSystem(gameRootPath, isZeroHour, _logger, cancellationToken: ct);
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

                    var calculated = $"0x{crc.Value:X8}";
                    IniCrcCache[cacheKey] = (freshness.MaxTicks, freshness.TotalLength, freshness.FileCount, calculated);
                    return OperationResult<string>.CreateSuccess(calculated);
                },
                ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<string>.CreateFailure($"Failed to calculate INI CRC: {ex.Message}");
        }
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

    private static (int Major, int Minor) ResolveVersion(
        byte[] exeBytes,
        string executablePath,
        int? major,
        int? minor,
        GameType? gameType = null)
    {
        int detectedMajor;
        int detectedMinor;
        if (PeVersionExtractor.TryExtract(exeBytes, out int extractedMajor, out int extractedMinor) ||
            PeVersionExtractor.TryExtractFromVersionInfo(executablePath, out extractedMajor, out extractedMinor))
        {
            detectedMajor = extractedMajor;
            detectedMinor = extractedMinor;
        }
        else if (gameType.HasValue)
        {
            detectedMajor = 1;
            detectedMinor = gameType.Value == GameType.ZeroHour ? 4 : 8;
        }
        else
        {
            // Fallback based on executable filename and immediate parent directory convention
            var fileName = Path.GetFileName(executablePath).ToLowerInvariant();
            var dirName = Path.GetFileName(Path.GetDirectoryName(executablePath) ?? string.Empty).ToLowerInvariant();
            if (fileName.Contains("zh") || fileName.Contains("generalsmd") ||
                dirName.Contains("zerohour") || dirName.Contains("zero hour") || dirName.Contains("zh"))
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

    private static (long SkirmishTicks, long SkirmishLength, long MpTicks, long MpLength) GetScriptsSignature(string root)
    {
        long sTicks = 0, sLen = 0, mTicks = 0, mLen = 0;
        if (!string.IsNullOrEmpty(root))
        {
            var sPath = Path.Combine(root, SageChecksumConstants.SkirmishScriptsRelativePath);
            if (File.Exists(sPath))
            {
                var fi = new FileInfo(sPath);
                sTicks = fi.LastWriteTimeUtc.Ticks;
                sLen = fi.Length;
            }

            var mPath = Path.Combine(root, SageChecksumConstants.MultiplayerScriptsRelativePath);
            if (File.Exists(mPath))
            {
                var fi = new FileInfo(mPath);
                mTicks = fi.LastWriteTimeUtc.Ticks;
                mLen = fi.Length;
            }
        }

        return (sTicks, sLen, mTicks, mLen);
    }

    private static (long MaxTicks, long TotalLength, int FileCount) GetIniFreshnessSignature(
        string gameRootPath,
        IReadOnlyList<string>? sideloadPaths,
        string? modPath)
    {
        var accumulator = new FreshnessAccumulator();

        UpdateFreshnessFromPath(accumulator, gameRootPath);

        if (sideloadPaths != null)
        {
            foreach (var sideload in sideloadPaths)
            {
                UpdateFreshnessFromPath(accumulator, sideload);
            }
        }

        if (!string.IsNullOrWhiteSpace(modPath))
        {
            UpdateFreshnessFromPath(accumulator, modPath);
        }

        return (accumulator.MaxTicks, accumulator.TotalLength, accumulator.FileCount);
    }

    private static void UpdateFreshnessFromPath(FreshnessAccumulator accumulator, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                UpdateFreshnessFromFile(accumulator, path);
            }
            else if (Directory.Exists(path))
            {
                UpdateFreshnessFromDirectory(accumulator, path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Suppress transient I/O exceptions during signature calculation
        }
    }

    private static void UpdateFreshnessFromFile(FreshnessAccumulator accumulator, string filePath)
    {
        var fi = new FileInfo(filePath);
        accumulator.AddFile(fi);
    }

    private static void UpdateFreshnessFromDirectory(FreshnessAccumulator accumulator, string directoryPath)
    {
        var di = new DirectoryInfo(directoryPath);
        accumulator.ObserveTicks(di.LastWriteTimeUtc.Ticks);

        UpdateFreshnessFromBigFiles(accumulator, di);
        UpdateFreshnessFromDataIni(accumulator, directoryPath);
    }

    private static void UpdateFreshnessFromBigFiles(FreshnessAccumulator accumulator, DirectoryInfo directoryInfo)
    {
        foreach (var file in directoryInfo.EnumerateFiles(SageChecksumConstants.BigFileSearchPattern, SearchOption.AllDirectories))
        {
            accumulator.AddFile(file);
        }
    }

    private static void UpdateFreshnessFromDataIni(FreshnessAccumulator accumulator, string directoryPath)
    {
        var dataIniPath = Path.Combine(directoryPath, "Data", "INI");
        if (!Directory.Exists(dataIniPath))
        {
            return;
        }

        var dataIniInfo = new DirectoryInfo(dataIniPath);
        accumulator.ObserveTicks(dataIniInfo.LastWriteTimeUtc.Ticks);

        foreach (var file in dataIniInfo.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            accumulator.AddFile(file);
        }
    }

    private static bool AddScriptFiles(LegacyChecksum crc, string root)
    {
        if (string.IsNullOrEmpty(root))
        {
            return true;
        }

        bool success = true;
        string skirmishPath = Path.Combine(root, SageChecksumConstants.SkirmishScriptsRelativePath);
        if (File.Exists(skirmishPath))
        {
            success &= TryAddFileBytes(crc, skirmishPath);
        }

        string mpPath = Path.Combine(root, SageChecksumConstants.MultiplayerScriptsRelativePath);
        if (File.Exists(mpPath))
        {
            success &= TryAddFileBytes(crc, mpPath);
        }

        return success;
    }

    private static bool TryAddFileBytes(LegacyChecksum crc, string path)
    {
        try
        {
            crc.Add(File.ReadAllBytes(path));
            return true;
        }
        catch (IOException)
        {
            // Transient read failure per specific exception convention
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // Transient read failure per specific exception convention
            return false;
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
