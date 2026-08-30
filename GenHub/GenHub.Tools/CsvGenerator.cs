using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using GenHub.Core.Constants;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Tools;

/// <summary>
/// Generates authoritative CSV catalog files from game installation directories.
/// </summary>
/// <param name="logger">The logger instance.</param>
public class CsvGenerator(ILogger logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Normalizes game type string to canonical "Generals" or "ZeroHour".
    /// </summary>
    /// <param name="gameType">The input game type string.</param>
    /// <returns>Canonical game type or empty string if invalid.</returns>
    public static string NormalizeGameType(string? gameType)
    {
        if (string.IsNullOrWhiteSpace(gameType))
        {
            return string.Empty;
        }

        var trimmed = gameType.Trim();
        if (trimmed.Equals(CsvConstants.GeneralsGameType, StringComparison.OrdinalIgnoreCase))
        {
            return CsvConstants.GeneralsGameType;
        }

        if (trimmed.Equals(CsvConstants.ZeroHourGameType, StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("ZH", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Zero Hour", StringComparison.OrdinalIgnoreCase))
        {
            return CsvConstants.ZeroHourGameType;
        }

        return string.Empty;
    }

    /// <summary>
    /// Normalizes language string to standard canonical code.
    /// </summary>
    /// <param name="language">The input language string.</param>
    /// <returns>Canonical uppercase language code.</returns>
    public static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return CsvConstants.LanguageEn;
        }

        var upper = language.Trim().ToUpperInvariant();
        return upper switch
        {
            "DE" or "GERMAN" or "DEUTSCH" => CsvConstants.LanguageDe,
            "FR" or "FRENCH" or "FRANCAIS" => CsvConstants.LanguageFr,
            "ES" or "SPANISH" or "ESPANOL" => CsvConstants.LanguageEs,
            "IT" or "ITALIAN" or "ITALIANO" => CsvConstants.LanguageIt,
            "KO" or "KOREAN" => CsvConstants.LanguageKo,
            "PL" or "POLISH" or "POLSKI" => CsvConstants.LanguagePl,
            "PT-BR" or "PT_BR" or "PTBR" or "PORTUGUESE" or "PORTUGUESEBRAZIL" => CsvConstants.LanguagePtBr,
            "ZH-CN" or "ZH_CN" or "ZHCN" or "CHINESE" or "CHINESESIMPLIFIED" or "SIMPLIFIEDCHINESE" => CsvConstants.LanguageZhCn,
            "ZH-TW" or "ZH_TW" or "ZHTW" or "CHINESETRADITIONAL" or "TRADITIONALCHINESE" => CsvConstants.LanguageZhTw,
            "ALL" => CsvConstants.AllLanguagesFilter,
            _ => upper,
        };
    }

    /// <summary>
    /// Generates a CSV file based on the provided generator options.
    /// </summary>
    /// <param name="options">The generator options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An <see cref="OperationResult{CsvGenerationSummary}"/> indicating success or failure.</returns>
    public async Task<OperationResult<CsvGenerationSummary>> GenerateCsvFileAsync(
        CsvGeneratorOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var startTime = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(options.InstallDir))
        {
            return OperationResult<CsvGenerationSummary>.CreateFailure("Installation directory must be specified.");
        }

        if (string.IsNullOrWhiteSpace(options.OutputPath))
        {
            return OperationResult<CsvGenerationSummary>.CreateFailure("Output path must be specified.");
        }

        if (!Directory.Exists(options.InstallDir))
        {
            return OperationResult<CsvGenerationSummary>.CreateFailure($"Installation directory not found: {options.InstallDir}");
        }

        var normalizedGameType = NormalizeGameType(options.GameType);
        if (string.IsNullOrWhiteSpace(normalizedGameType))
        {
            return OperationResult<CsvGenerationSummary>.CreateFailure($"Invalid game type: '{options.GameType}'. Must be 'Generals' or 'ZeroHour'.");
        }

        var normalizedLanguage = NormalizeLanguage(options.Language);

        logger.LogInformation(
            "Scanning directory: {Path} for {GameType} {Version} (Language: {Language})",
            options.InstallDir,
            normalizedGameType,
            options.Version,
            normalizedLanguage);

        try
        {
            var (entries, filesScanned) = await ScanInstallationAsync(
                options.InstallDir,
                normalizedGameType,
                normalizedLanguage,
                options.DownloadUrl,
                options.OutputPath,
                cancellationToken);

            var outputDir = Path.GetDirectoryName(options.OutputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            await WriteCsvFileAsync(entries, options.OutputPath, cancellationToken);
            logger.LogInformation("Generated CSV file: {Path} with {Count} entries", options.OutputPath, entries.Count);

            var (csvMd5, csvSha256, csvSize) = await CalculateFileHashAndSizeAsync(options.OutputPath, cancellationToken);

            var indexUpdated = false;
            if (options.UpdateIndex)
            {
                var indexPath = !string.IsNullOrWhiteSpace(options.IndexFilePath)
                    ? options.IndexFilePath
                    : Path.Combine(outputDir ?? string.Empty, "index.json");

                await UpdateIndexFileAsync(options, indexPath, entries.Count, csvSize, csvMd5, csvSha256, cancellationToken);
                indexUpdated = true;
            }

            var summary = new CsvGenerationSummary(
                TotalFilesScanned: filesScanned,
                TotalEntriesWritten: entries.Count,
                TotalSizeBytes: csvSize,
                CsvPath: Path.GetFullPath(options.OutputPath),
                CsvMd5: csvMd5,
                CsvSha256: csvSha256,
                IndexUpdated: indexUpdated);

            return OperationResult<CsvGenerationSummary>.CreateSuccess(summary, DateTime.UtcNow - startTime);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate CSV file: {Error}", ex.Message);
            return OperationResult<CsvGenerationSummary>.CreateFailure($"CSV generation failed: {ex.Message}", DateTime.UtcNow - startTime);
        }
    }

    private static bool IsLanguageSpecific(string relativePath)
    {
        // Check for Language folder
        if (relativePath.StartsWith(LanguageDirectoryNames.DataLang, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check for language-specific directory patterns
        var languageDirectories = new[]
        {
            LanguageDirectoryNames.DataEnglish, LanguageDirectoryNames.DataEnglishUppercase,
            LanguageDirectoryNames.DataGerman, LanguageDirectoryNames.DataDeutsch,
            LanguageDirectoryNames.DataFrench,
            LanguageDirectoryNames.DataSpanish,
            LanguageDirectoryNames.DataItalian,
            LanguageDirectoryNames.DataKorean,
            LanguageDirectoryNames.DataPolish,
            LanguageDirectoryNames.DataPortuguese,
            LanguageDirectoryNames.DataChinese,
            LanguageDirectoryNames.DataChineseTraditional,
            "Data/chinese traditional",
            "Data/Chinese Traditional",
        };

        if (languageDirectories.Any(dir => relativePath.StartsWith(dir, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Check for language-specific .big file patterns
        var languageFilePatterns = new[]
        {
            LanguageFilePatterns.EnglishBig, LanguageFilePatterns.AudioEnglishBig, LanguageFilePatterns.SpeechEnglishBig, LanguageFilePatterns.EnglishZHBig,
            LanguageFilePatterns.GermanBig, LanguageFilePatterns.AudioGermanBig, LanguageFilePatterns.GermanZHBig,
            LanguageFilePatterns.FrenchBig, LanguageFilePatterns.AudioFrenchBig, LanguageFilePatterns.FrenchZHBig,
            LanguageFilePatterns.SpanishBig, LanguageFilePatterns.AudioSpanishBig, LanguageFilePatterns.SpanishZHBig,
            LanguageFilePatterns.ItalianBig, LanguageFilePatterns.AudioItalianBig, LanguageFilePatterns.ItalianZHBig,
            LanguageFilePatterns.KoreanBig, LanguageFilePatterns.AudioKoreanBig, LanguageFilePatterns.KoreanZHBig,
            LanguageFilePatterns.PolishBig, LanguageFilePatterns.AudioPolishBig, LanguageFilePatterns.PolishZHBig,
            LanguageFilePatterns.PortugueseBrazilBig, LanguageFilePatterns.AudioPortugueseBrazilBig, LanguageFilePatterns.PortugueseZHBig,
            LanguageFilePatterns.ChineseBig, LanguageFilePatterns.AudioChineseBig, LanguageFilePatterns.ChineseZHBig,
            LanguageFilePatterns.ChineseTraditionalBig, LanguageFilePatterns.AudioChineseTraditionalBig,
        };

        if (languageFilePatterns.Any(pattern => relativePath.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Check for language-specific INI files (e.g., English.ini, German.ini, etc.)
        if (relativePath.StartsWith(LanguageDirectoryNames.DataIni, StringComparison.OrdinalIgnoreCase) &&
            relativePath.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(relativePath);
            var languageInis = new[]
            {
                LanguageFilePatterns.EnglishIni, LanguageFilePatterns.GermanIni, LanguageFilePatterns.FrenchIni, LanguageFilePatterns.SpanishIni,
                LanguageFilePatterns.ItalianIni, LanguageFilePatterns.KoreanIni, LanguageFilePatterns.PolishIni,
                LanguageFilePatterns.PortugueseBrazilIni, LanguageFilePatterns.PortugueseIni,
                LanguageFilePatterns.ChineseIni, LanguageFilePatterns.ChineseTraditionalIni,
            };

            if (languageInis.Any(ln => fileName.Equals(ln, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    [SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms", Justification = "MD5 hash is required by the CSV catalog format for backward compatibility.")]
    [SuppressMessage("Security", "S4790:Make sure this weak hash algorithm is not used in a sensitive cryptographic context", Justification = "MD5 hash is required for legacy game file checksum comparison.")]
    private static async Task<(string Md5, string Sha256)> CalculateHashesAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, IoConstants.DefaultFileBufferSize, useAsync: true);
        using var md5 = MD5.Create();
        using var sha256 = SHA256.Create();

        var buffer = new byte[IoConstants.DefaultFileBufferSize];
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            md5.TransformBlock(buffer, 0, bytesRead, null, 0);
            sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
        }

        md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

        return (
            Convert.ToHexString(md5.Hash!).ToLowerInvariant(),
            Convert.ToHexString(sha256.Hash!).ToLowerInvariant());
    }

    [SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms", Justification = "MD5 hash is required by the CSV catalog format for backward compatibility.")]
    [SuppressMessage("Security", "S4790:Make sure this weak hash algorithm is not used in a sensitive cryptographic context", Justification = "MD5 hash is required for legacy game file checksum comparison.")]
    private static async Task<(string Md5, string Sha256, long SizeBytes)> CalculateFileHashAndSizeAsync(string filePath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        var (md5, sha256) = await CalculateHashesAsync(filePath, cancellationToken);
        return (md5, sha256, fileInfo.Length);
    }

    private static bool IsRequiredFile(string relativePath)
    {
        // Language-agnostic required files - core game files
        var coreRequiredFiles = new[]
        {
            GameClientConstants.GameExecutable,
            GameClientConstants.SteamGameDatExecutable,
            "ZeroHour.exe",
            "generals.exe",
        };

        if (coreRequiredFiles.Any(rf => relativePath.EndsWith(rf, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Language-specific INI files (e.g., English.ini, German.ini, French.ini, etc.)
        if (relativePath.StartsWith(LanguageDirectoryNames.DataIni, StringComparison.OrdinalIgnoreCase) &&
            relativePath.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(relativePath);
            var languageNames = new[]
            {
                LanguageFilePatterns.EnglishIni, LanguageFilePatterns.GermanIni, LanguageFilePatterns.FrenchIni, LanguageFilePatterns.SpanishIni,
                LanguageFilePatterns.ItalianIni, LanguageFilePatterns.KoreanIni, LanguageFilePatterns.PolishIni,
                LanguageFilePatterns.PortugueseBrazilIni, LanguageFilePatterns.PortugueseIni,
                LanguageFilePatterns.ChineseIni, LanguageFilePatterns.ChineseTraditionalIni,
            };

            if (languageNames.Any(ln => fileName.Equals(ln, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        // Language-specific string files
        if (relativePath.StartsWith(LanguageDirectoryNames.DataLang, StringComparison.OrdinalIgnoreCase) &&
            relativePath.EndsWith(LanguageFilePatterns.GameStr, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string GetFileMetadata(string relativePath)
    {
        var metadata = new Dictionary<string, string>();

        if (relativePath.StartsWith(LanguageDirectoryNames.DataIni, StringComparison.OrdinalIgnoreCase) ||
            relativePath.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
        {
            metadata["category"] = FileCategoryConstants.Config;
        }
        else if (relativePath.StartsWith(LanguageDirectoryNames.DataLang, StringComparison.OrdinalIgnoreCase) ||
                 relativePath.EndsWith(LanguageFilePatterns.GameStr, StringComparison.OrdinalIgnoreCase))
        {
            metadata["category"] = FileCategoryConstants.Language;
        }
        else if (relativePath.StartsWith(LanguageDirectoryNames.DataMap, StringComparison.OrdinalIgnoreCase) ||
                 relativePath.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
        {
            metadata["category"] = FileCategoryConstants.Maps;
        }
        else if (relativePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                 relativePath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            metadata["category"] = FileCategoryConstants.Audio;
        }
        else if (relativePath.EndsWith(".w3d", StringComparison.OrdinalIgnoreCase) ||
                 relativePath.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ||
                 relativePath.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
        {
            metadata["category"] = FileCategoryConstants.Graphics;
        }
        else
        {
            metadata["category"] = FileCategoryConstants.Other;
        }

        return JsonSerializer.Serialize(metadata);
    }

    private static async Task WriteCsvFileAsync(
        IReadOnlyList<CsvCatalogEntry> entries,
        string csvPath,
        CancellationToken cancellationToken)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
        };

        await using var writer = new StreamWriter(csvPath);
        await using var csv = new CsvWriter(writer, config);
        await csv.WriteRecordsAsync(entries, cancellationToken);
    }

    private async Task<(List<CsvCatalogEntry> Entries, int FilesScanned)> ScanInstallationAsync(
        string installationPath,
        string gameType,
        string languageCode,
        string? downloadUrlOverride,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var entries = new List<CsvCatalogEntry>();
        var files = Directory.GetFiles(installationPath, "*", SearchOption.AllDirectories);
        var totalFiles = files.Length;

        logger.LogInformation("Scanning {Count} files in {Path}", totalFiles, installationPath);

        for (var i = 0; i < totalFiles; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[i];
            if (i > 0 && i % 100 == 0)
            {
                logger.LogInformation("Processed {Current}/{Total} files", i, totalFiles);
            }

            try
            {
                var entry = await CreateCsvEntryAsync(file, installationPath, gameType, languageCode, downloadUrlOverride, outputPath, cancellationToken);
                if (entry != null)
                {
                    entries.Add(entry);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to process file: {Path}", file);
            }
        }

        return (entries.OrderBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase).ToList(), totalFiles);
    }

    private async Task<CsvCatalogEntry?> CreateCsvEntryAsync(
        string filePath,
        string installationPath,
        string gameType,
        string defaultLanguage,
        string? downloadUrlOverride,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var relativePath = Path.GetRelativePath(installationPath, filePath).Replace('\\', '/');
        var fileInfo = new FileInfo(filePath);

        if (fileInfo.Length == 0)
        {
            return null; // Skip empty files
        }

        var (md5, sha256) = await CalculateHashesAsync(filePath, cancellationToken);
        var isSpecific = IsLanguageSpecific(relativePath);
        var outputFileName = Path.GetFileName(outputPath);

        var downloadUrl = !string.IsNullOrWhiteSpace(downloadUrlOverride)
            ? downloadUrlOverride
            : $"https://raw.githubusercontent.com/community-outpost/GenHub/main/docs/GameInstallationFilesRegistry/{outputFileName}";

        return new CsvCatalogEntry
        {
            RelativePath = relativePath,
            Size = fileInfo.Length,
            Md5 = md5,
            Sha256 = sha256,
            GameType = gameType,
            Language = isSpecific ? defaultLanguage : CsvConstants.AllLanguagesFilter,
            IsRequired = IsRequiredFile(relativePath),
            Metadata = GetFileMetadata(relativePath),
            DownloadUrl = downloadUrl,
        };
    }

    private async Task UpdateIndexFileAsync(
        CsvGeneratorOptions options,
        string indexPath,
        int entryCount,
        long totalSizeBytes,
        string csvMd5,
        string csvSha256,
        CancellationToken cancellationToken)
    {
        CsvCatalogRegistryIndex index;
        if (File.Exists(indexPath))
        {
            var content = await File.ReadAllTextAsync(indexPath, cancellationToken);
            index = JsonSerializer.Deserialize<CsvCatalogRegistryIndex>(content) ?? new CsvCatalogRegistryIndex();
        }
        else
        {
            index = new CsvCatalogRegistryIndex
            {
                Description = "Index of CSV registries for Command & Conquer Generals and Zero Hour validation",
            };
        }

        index.Version = "1.0.0";
        index.LastUpdatedAt = DateTime.UtcNow;

        var targetId = $"{options.GameType.ToLowerInvariant()}-{options.Version.ToLowerInvariant()}";
        var existingEntry = index.Entries.FirstOrDefault(e => e.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase));

        var outputFileName = Path.GetFileName(options.OutputPath);
        var entryUrl = !string.IsNullOrWhiteSpace(options.DownloadUrl)
            ? options.DownloadUrl
            : $"https://raw.githubusercontent.com/community-outpost/GenHub/main/docs/GameInstallationFilesRegistry/{outputFileName}";

        if (existingEntry != null)
        {
            existingEntry.GameType = options.GameType;
            existingEntry.Version = options.Version;
            existingEntry.Url = entryUrl;
            existingEntry.FileCount = entryCount;
            existingEntry.TotalSizeBytes = totalSizeBytes;
            existingEntry.Checksum = new Checksum { Md5 = csvMd5, Sha256 = csvSha256 };
            existingEntry.GeneratedAt = DateTime.UtcNow;
            existingEntry.GeneratorVersion = "1.0.0";
            existingEntry.IsActive = true;
        }
        else
        {
            index.Entries.Add(new CsvCatalogRegistryEntry
            {
                Id = targetId,
                GameType = options.GameType,
                Version = options.Version,
                Url = entryUrl,
                FileCount = entryCount,
                TotalSizeBytes = totalSizeBytes,
                SupportedLanguages =
                [
                    CsvConstants.AllLanguagesFilter,
                    CsvConstants.LanguageEn,
                    CsvConstants.LanguageDe,
                    CsvConstants.LanguageFr,
                    CsvConstants.LanguageEs,
                    CsvConstants.LanguageIt,
                    CsvConstants.LanguageKo,
                    CsvConstants.LanguagePl,
                    CsvConstants.LanguagePtBr,
                    CsvConstants.LanguageZhCn,
                    CsvConstants.LanguageZhTw,
                ],
                Checksum = new Checksum { Md5 = csvMd5, Sha256 = csvSha256 },
                GeneratedAt = DateTime.UtcNow,
                GeneratorVersion = "1.0.0",
                IsActive = true,
            });
        }

        var indexDir = Path.GetDirectoryName(indexPath);
        if (!string.IsNullOrEmpty(indexDir) && !Directory.Exists(indexDir))
        {
            Directory.CreateDirectory(indexDir);
        }

        var json = JsonSerializer.Serialize(index, JsonOptions);
        await File.WriteAllTextAsync(indexPath, json, cancellationToken);
        logger.LogInformation("Updated index.json metadata at: {Path} (Entry: {Id})", indexPath, targetId);
    }
}
