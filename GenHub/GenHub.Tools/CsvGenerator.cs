using System.Security.Cryptography;
using CsvHelper;
using CsvHelper.Configuration;
using GenHub.Core.Constants;
using GenHub.Core.Models.Content;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace GenHub.Tools;

/// <summary>
/// Generates CSV files from game installations.
/// </summary>
internal class CsvGenerator
{
    private readonly Dictionary<string, string> arguments;
    private readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvGenerator"/> class.
    /// </summary>
    /// <param name="arguments">The command line arguments.</param>
    /// <param name="logger">The logger.</param>
    public CsvGenerator(Dictionary<string, string> arguments, ILogger logger)
    {
        this.arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Generates the CSV file based on arguments.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task GenerateCsvFileAsync()
    {
        var installDir = this.arguments["installDir"];
        var output = this.arguments["output"];
        var gameType = this.arguments["gameType"];
        var version = this.arguments["version"];
        var language = this.arguments["language"];

        if (!Directory.Exists(installDir))
        {
            throw new DirectoryNotFoundException($"Installation directory not found: {installDir}");
        }

        this.logger.LogInformation("Scanning directory: {Path}", installDir);

        var entries = await this.ScanInstallationAsync(installDir, gameType, language);

        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(output);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        await this.WriteCsvFileAsync(entries, output);
        this.logger.LogInformation("Generated CSV file: {Path} with {Count} entries", output, entries.Count);
    }

    private async Task<List<CsvCatalogEntry>> ScanInstallationAsync(string installationPath, string gameType, string languageCode)
    {
        var entries = new List<CsvCatalogEntry>();
        var files = Directory.GetFiles(installationPath, "*", SearchOption.AllDirectories);
        var totalFiles = files.Length;

        this.logger.LogInformation("Scanning {Count} files in {Path}", totalFiles, installationPath);

        for (var i = 0; i < totalFiles; i++)
        {
            var file = files[i];
            if (i % 100 == 0)
            {
                this.logger.LogInformation("Processed {Current}/{Total} files", i, totalFiles);
            }

            try
            {
                var entry = await this.CreateCsvEntryAsync(file, installationPath, gameType, languageCode);
                if (entry != null)
                {
                    entries.Add(entry);
                }
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, "Failed to process file: {Path}", file);
            }
        }

        return entries.OrderBy(e => e.RelativePath).ToList();
    }

    private async Task<CsvCatalogEntry?> CreateCsvEntryAsync(string filePath, string installationPath, string gameType, string defaultLanguage)
    {
        var relativePath = Path.GetRelativePath(installationPath, filePath).Replace('\\', '/');
        var fileInfo = new FileInfo(filePath);

        if (fileInfo.Length == 0)
        {
            return null; // Skip empty files
        }

        var (md5, sha256) = await this.CalculateHashesAsync(filePath);
        var isSpecific = this.IsLanguageSpecific(relativePath);

        return new CsvCatalogEntry
        {
            RelativePath = relativePath,
            Size = fileInfo.Length,
            Md5 = md5,
            Sha256 = sha256,
            GameType = gameType,
            Language = isSpecific ? defaultLanguage : "All",
            IsRequired = this.IsRequiredFile(relativePath),
            Metadata = this.GetFileMetadata(relativePath),
        };
    }

    private bool IsLanguageSpecific(string relativePath)
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
        };

        foreach (var dir in languageDirectories)
        {
            if (relativePath.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Check for language-specific .big file patterns
        // Format: {Language}.big, Audio{Language}.big, Speech{Language}.big
        // Supported: EN, DE, FR, ES, IT, KO, PL, PT-BR, ZH-CN, ZH-TW
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

        foreach (var pattern in languageFilePatterns)
        {
            if (relativePath.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<(string Md5, string Sha256)> CalculateHashesAsync(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var md5 = MD5.Create();
        using var sha256 = SHA256.Create();

        var buffer = new byte[IoConstants.DefaultFileBufferSize];
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
        {
            md5.TransformBlock(buffer, 0, bytesRead, null, 0);
            sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
        }

        md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

        return (
            BitConverter.ToString(md5.Hash!).Replace("-", string.Empty).ToLowerInvariant(),
            BitConverter.ToString(sha256.Hash!).Replace("-", string.Empty).ToLowerInvariant());
    }

    private bool IsRequiredFile(string relativePath)
    {
        // Language-agnostic required files - core game files
        var coreRequiredFiles = new[]
        {
            GameClientConstants.GameExecutable,
            GameClientConstants.SteamGameDatExecutable,
        };

        if (coreRequiredFiles.Any(rf => relativePath.EndsWith(rf, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Language-specific INI files (e.g., English.ini, German.ini, French.ini, etc.)
        // Pattern: Data/INI/{Language}.ini
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

        // Language-specific string files (e.g., Data/Lang/English/game.str, Data/Lang/German/game.str)
        // Pattern: Data/Lang/{Language}/game.str
        if (relativePath.StartsWith(LanguageDirectoryNames.DataLang, StringComparison.OrdinalIgnoreCase) &&
            relativePath.EndsWith(LanguageFilePatterns.GameStr, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private string GetFileMetadata(string relativePath)
    {
        var metadata = new Dictionary<string, string>();

        if (relativePath.StartsWith(LanguageDirectoryNames.DataIni, StringComparison.OrdinalIgnoreCase))
        {
            metadata["category"] = FileCategoryConstants.Config;
        }
        else if (relativePath.StartsWith(LanguageDirectoryNames.DataLang, StringComparison.OrdinalIgnoreCase))
        {
            metadata["category"] = FileCategoryConstants.Language;
        }
        else if (relativePath.StartsWith(LanguageDirectoryNames.DataMap, StringComparison.OrdinalIgnoreCase))
        {
            metadata["category"] = FileCategoryConstants.Maps;
        }
        else if (relativePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                 relativePath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            metadata["category"] = FileCategoryConstants.Audio;
        }
        else if (relativePath.EndsWith(".w3d", StringComparison.OrdinalIgnoreCase) ||
                 relativePath.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
        {
            metadata["category"] = FileCategoryConstants.Graphics;
        }
        else
        {
            metadata["category"] = FileCategoryConstants.Other;
        }

        return JsonSerializer.Serialize(metadata);
    }

    private async Task WriteCsvFileAsync(List<CsvCatalogEntry> entries, string csvPath)
    {
        var config = new CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            PrepareHeaderForMatch = args => args.Header.ToLower(), // This is for reading
        };

        // Custom map for writing is better
        await using var writer = new StreamWriter(csvPath);
        await using var csv = new CsvWriter(writer, config);
        await csv.WriteRecordsAsync(entries);
    }
}
