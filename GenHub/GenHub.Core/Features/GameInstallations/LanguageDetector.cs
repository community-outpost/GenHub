using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Features.GameInstallations;

/// <summary>
/// Detects the language of a Command & Conquer Generals or Zero Hour installation.
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
        if (!Directory.Exists(installationPath))
        {
            return Task.FromResult("EN"); // Fallback
        }

        // Check for language-specific directories and files
        var languageMappings = new[]
        {
            new { Pattern = "Data\\english", Language = "EN" },
            new { Pattern = "Data\\English", Language = "EN" },
            new { Pattern = "Data\\german", Language = "DE" },
            new { Pattern = "Data\\deutsch", Language = "DE" },
            new { Pattern = "Data\\french", Language = "FR" },
            new { Pattern = "Data\\spanish", Language = "ES" },
            new { Pattern = "Data\\italian", Language = "IT" },
            new { Pattern = "Data\\korean", Language = "KO" },
            new { Pattern = "Data\\polish", Language = "PL" },
            new { Pattern = "Data\\portuguese", Language = "PT-BR" },
            new { Pattern = "Data\\chinese", Language = "ZH-CN" },
            new { Pattern = "Data\\chinese-traditional", Language = "ZH-TW" },
        };

        foreach (var mapping in languageMappings)
        {
            if (Directory.Exists(Path.Combine(installationPath, mapping.Pattern)))
            {
                return Task.FromResult(mapping.Language);
            }
        }

        // Check for language-specific files
        var fileMappings = new[]
        {
            // English
            new { Pattern = "English.big", Language = "EN" },
            new { Pattern = "AudioEnglish.big", Language = "EN" },
            new { Pattern = "SpeechEnglish.big", Language = "EN" },

            // German
            new { Pattern = "German.big", Language = "DE" },
            new { Pattern = "AudioGerman.big", Language = "DE" },

            // French
            new { Pattern = "French.big", Language = "FR" },
            new { Pattern = "AudioFrench.big", Language = "FR" },

            // Spanish
            new { Pattern = "Spanish.big", Language = "ES" },
            new { Pattern = "AudioSpanish.big", Language = "ES" },

            // Italian
            new { Pattern = "Italian.big", Language = "IT" },
            new { Pattern = "AudioItalian.big", Language = "IT" },

            // Korean
            new { Pattern = "Korean.big", Language = "KO" },
            new { Pattern = "AudioKorean.big", Language = "KO" },

            // Polish
            new { Pattern = "Polish.big", Language = "PL" },
            new { Pattern = "AudioPolish.big", Language = "PL" },

            // Portuguese-Brazil
            new { Pattern = "PortugueseBrazil.big", Language = "PT-BR" },
            new { Pattern = "AudioPortugueseBrazil.big", Language = "PT-BR" },

            // Chinese Simplified
            new { Pattern = "Chinese.big", Language = "ZH-CN" },
            new { Pattern = "AudioChinese.big", Language = "ZH-CN" },

            // Chinese Traditional
            new { Pattern = "ChineseTraditional.big", Language = "ZH-TW" },
            new { Pattern = "AudioChineseTraditional.big", Language = "ZH-TW" },
        };

        foreach (var mapping in fileMappings)
        {
            if (File.Exists(Path.Combine(installationPath, mapping.Pattern)))
            {
                return Task.FromResult(mapping.Language);
            }
        }

        // Check for Zero Hour specific patterns
        var zhPatterns = new[]
        {
            new { Pattern = "EnglishZH.big", Language = "EN" },
            new { Pattern = "AudioZH.big", Language = "EN" },
            new { Pattern = "INIZH.big", Language = "EN" },
            new { Pattern = "*ZH.big", Language = "EN" }, // Generic ZH files
            new { Pattern = "GeneralsOnlineZH", Language = "EN" }, // Executables
            new { Pattern = "GermanZH.big", Language = "DE" },
            new { Pattern = "FrenchZH.big", Language = "FR" },
            new { Pattern = "SpanishZH.big", Language = "ES" },
            new { Pattern = "ItalianZH.big", Language = "IT" },
            new { Pattern = "KoreanZH.big", Language = "KO" },
            new { Pattern = "PolishZH.big", Language = "PL" },
            new { Pattern = "PortugueseZH.big", Language = "PT-BR" },
            new { Pattern = "ChineseZH.big", Language = "ZH-CN" },
        };

        foreach (var mapping in zhPatterns)
        {
            if (mapping.Pattern.Contains("*"))
            {
                // Handle wildcard
                var files = Directory.GetFiles(installationPath, mapping.Pattern, SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    return Task.FromResult(mapping.Language);
                }
            }
            else if (File.Exists(Path.Combine(installationPath, mapping.Pattern)))
            {
                return Task.FromResult(mapping.Language);
            }
        }

        // Fallback to English
        return Task.FromResult("EN");
    }
}
