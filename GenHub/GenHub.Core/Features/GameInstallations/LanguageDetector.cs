using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Models.Content;

namespace GenHub.Core.Features.GameInstallations;

/// <summary>
/// Detects the language of a Command &amp; Conquer Generals or Zero Hour installation.
/// </summary>
public class LanguageDetector : ILanguageDetector
{
    /// <summary>
    /// Detects the language of a game installation at the specified path.
    /// </summary>
    /// <param name="installationPath">The path to the game installation directory.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The detected language code in uppercase (e.g., "EN", "DE"), or "EN" as fallback.</returns>
    public Task<string> DetectAsync(string installationPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(installationPath) || !Directory.Exists(installationPath))
        {
            return Task.FromResult(CsvConstants.LanguageEn);
        }

        // Check for language-specific directories
        var directoryMappings = new (string RelativeDir, string Language)[]
        {
            (LanguageDirectoryNames.DataEnglish, CsvConstants.LanguageEn),
            (LanguageDirectoryNames.DataEnglishUppercase, CsvConstants.LanguageEn),
            (LanguageDirectoryNames.DataGerman, CsvConstants.LanguageDe),
            (LanguageDirectoryNames.DataDeutsch, CsvConstants.LanguageDe),
            (LanguageDirectoryNames.DataFrench, CsvConstants.LanguageFr),
            (LanguageDirectoryNames.DataSpanish, CsvConstants.LanguageEs),
            (LanguageDirectoryNames.DataItalian, CsvConstants.LanguageIt),
            (LanguageDirectoryNames.DataKorean, CsvConstants.LanguageKo),
            (LanguageDirectoryNames.DataPolish, CsvConstants.LanguagePl),
            (LanguageDirectoryNames.DataPortuguese, CsvConstants.LanguagePtBr),
            (LanguageDirectoryNames.DataChinese, CsvConstants.LanguageZhCn),
            (LanguageDirectoryNames.DataChineseTraditional, CsvConstants.LanguageZhTw),
        };

        foreach (var (relativeDir, language) in directoryMappings)
        {
            var dirPath = CombineRelativePath(installationPath, relativeDir);
            if (Directory.Exists(dirPath))
            {
                return Task.FromResult(ContentSearchQuery.NormalizeLanguage(language));
            }
        }

        // Check for language-specific files
        var fileMappings = new (string FileName, string Language)[]
        {
            // English
            (LanguageFilePatterns.EnglishBig, CsvConstants.LanguageEn),
            (LanguageFilePatterns.AudioEnglishBig, CsvConstants.LanguageEn),
            (LanguageFilePatterns.SpeechEnglishBig, CsvConstants.LanguageEn),

            // German
            (LanguageFilePatterns.GermanBig, CsvConstants.LanguageDe),
            (LanguageFilePatterns.AudioGermanBig, CsvConstants.LanguageDe),

            // French
            (LanguageFilePatterns.FrenchBig, CsvConstants.LanguageFr),
            (LanguageFilePatterns.AudioFrenchBig, CsvConstants.LanguageFr),

            // Spanish
            (LanguageFilePatterns.SpanishBig, CsvConstants.LanguageEs),
            (LanguageFilePatterns.AudioSpanishBig, CsvConstants.LanguageEs),

            // Italian
            (LanguageFilePatterns.ItalianBig, CsvConstants.LanguageIt),
            (LanguageFilePatterns.AudioItalianBig, CsvConstants.LanguageIt),

            // Korean
            (LanguageFilePatterns.KoreanBig, CsvConstants.LanguageKo),
            (LanguageFilePatterns.AudioKoreanBig, CsvConstants.LanguageKo),

            // Polish
            (LanguageFilePatterns.PolishBig, CsvConstants.LanguagePl),
            (LanguageFilePatterns.AudioPolishBig, CsvConstants.LanguagePl),

            // Portuguese-Brazil
            (LanguageFilePatterns.PortugueseBrazilBig, CsvConstants.LanguagePtBr),
            (LanguageFilePatterns.AudioPortugueseBrazilBig, CsvConstants.LanguagePtBr),

            // Chinese Simplified
            (LanguageFilePatterns.ChineseBig, CsvConstants.LanguageZhCn),
            (LanguageFilePatterns.AudioChineseBig, CsvConstants.LanguageZhCn),

            // Chinese Traditional
            (LanguageFilePatterns.ChineseTraditionalBig, CsvConstants.LanguageZhTw),
            (LanguageFilePatterns.AudioChineseTraditionalBig, CsvConstants.LanguageZhTw),
        };

        foreach (var (fileName, language) in fileMappings)
        {
            var filePath = Path.Combine(installationPath, fileName);
            if (File.Exists(filePath))
            {
                return Task.FromResult(ContentSearchQuery.NormalizeLanguage(language));
            }
        }

        // Check for Zero Hour specific patterns
        var zhPatterns = new (string Pattern, string Language)[]
        {
            (LanguageFilePatterns.EnglishZHBig, CsvConstants.LanguageEn),
            ("AudioZH.big", CsvConstants.LanguageEn),
            ("INIZH.big", CsvConstants.LanguageEn),
            (LanguageFilePatterns.GermanZHBig, CsvConstants.LanguageDe),
            (LanguageFilePatterns.FrenchZHBig, CsvConstants.LanguageFr),
            (LanguageFilePatterns.SpanishZHBig, CsvConstants.LanguageEs),
            (LanguageFilePatterns.ItalianZHBig, CsvConstants.LanguageIt),
            (LanguageFilePatterns.KoreanZHBig, CsvConstants.LanguageKo),
            (LanguageFilePatterns.PolishZHBig, CsvConstants.LanguagePl),
            (LanguageFilePatterns.PortugueseZHBig, CsvConstants.LanguagePtBr),
            (LanguageFilePatterns.ChineseZHBig, CsvConstants.LanguageZhCn),
            ("*ZH.big", CsvConstants.LanguageEn),
        };

        foreach (var (pattern, language) in zhPatterns)
        {
            if (pattern.Contains('*'))
            {
                try
                {
                    var files = Directory.GetFiles(installationPath, pattern, SearchOption.AllDirectories);
                    if (files.Length > 0)
                    {
                        return Task.FromResult(ContentSearchQuery.NormalizeLanguage(language));
                    }
                }
                catch (IOException)
                {
                    // Fall through on IO issues
                }
                catch (UnauthorizedAccessException)
                {
                    // Fall through on permission issues
                }
            }
            else
            {
                var filePath = Path.Combine(installationPath, pattern);
                if (File.Exists(filePath))
                {
                    return Task.FromResult(ContentSearchQuery.NormalizeLanguage(language));
                }
            }
        }

        // Fallback to English
        return Task.FromResult(CsvConstants.LanguageEn);
    }

    private static string CombineRelativePath(string basePath, string relativePath)
    {
        var segments = relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return Path.Combine(segments.Prepend(basePath).ToArray());
    }
}
