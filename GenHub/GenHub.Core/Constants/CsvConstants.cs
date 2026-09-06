using System;
using System.Diagnostics.CodeAnalysis;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants specific to CSV catalog discovery and content pipeline.
/// </summary>
[SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Centralized URI constants for remote catalog discovery")]
public static class CsvConstants
{
    /// <summary>
    /// Default remote index.json source for CSV catalog discovery.
    /// </summary>
    public const string DefaultIndexFileUrl = "https://raw.githubusercontent.com/community-outpost/GenHub/main/docs/GameInstallationFilesRegistry/index.json";

    /// <summary>
    /// Source name for the CSV catalog discoverer.
    /// </summary>
    public const string SourceName = "Csv Discoverer";

    /// <summary>
    /// Description for the CSV catalog discoverer.
    /// </summary>
    public const string Description = "Discovers base game manifests from verified CSV catalogs.";

    /// <summary>
    /// Source name for the CSV catalog content provider.
    /// </summary>
    public const string ProviderSourceName = "CSV Catalog Provider";

    /// <summary>
    /// Description for the CSV catalog content provider.
    /// </summary>
    public const string ProviderDescription = "Provides base game manifests from verified CSV catalogs.";

    /// <summary>
    /// Directory name for cached CSV indexes and catalogs.
    /// </summary>
    public const string CacheDirectoryName = "CsvCatalogs";

    /// <summary>
    /// File extension for cached CSV catalog content.
    /// </summary>
    public const string CacheFileExtension = ".cache";

    /// <summary>
    /// File extension for temporary CSV cache writes.
    /// </summary>
    public const string TemporaryCacheFileExtension = ".tmp";

    /// <summary>
    /// Number of days stale CSV catalog data is retained for offline fallback.
    /// </summary>
    public const int CacheRetentionDays = 30;

    /// <summary>
    /// Resolver ID for CSV catalog content.
    /// </summary>
    public const string ResolverId = "CSVResolver";

    /// <summary>
    /// Metadata key for the CSV source URL.
    /// </summary>
    public const string CsvUrlMetadataKey = "csvUrl";

    /// <summary>
    /// Metadata key for the game type.
    /// </summary>
    public const string GameTypeMetadataKey = "gameType";

    /// <summary>
    /// Metadata key for the game version.
    /// </summary>
    public const string VersionMetadataKey = "version";

    /// <summary>
    /// Metadata key for the content language.
    /// </summary>
    public const string LanguageMetadataKey = "language";

    /// <summary>
    /// Metadata key for the expected file count.
    /// </summary>
    public const string FileCountMetadataKey = "fileCount";

    /// <summary>
    /// String representation for Generals game type in CSV catalogs.
    /// </summary>
    public const string GeneralsGameType = "Generals";

    /// <summary>
    /// String representation for Zero Hour game type in CSV catalogs.
    /// </summary>
    public const string ZeroHourGameType = "ZeroHour";

    /// <summary>
    /// Special language filter value to include all languages.
    /// </summary>
    public const string AllLanguagesFilter = "All";

    /// <summary>
    /// Canonical language code for English.
    /// </summary>
    public const string LanguageEn = "EN";

    /// <summary>
    /// Canonical language code for German.
    /// </summary>
    public const string LanguageDe = "DE";

    /// <summary>
    /// Canonical language code for French.
    /// </summary>
    public const string LanguageFr = "FR";

    /// <summary>
    /// Canonical language code for Polish.
    /// </summary>
    public const string LanguagePl = "PL";

    /// <summary>
    /// Canonical language code for Spanish.
    /// </summary>
    public const string LanguageEs = "ES";

    /// <summary>
    /// Canonical language code for Italian.
    /// </summary>
    public const string LanguageIt = "IT";

    /// <summary>
    /// Canonical language code for Korean.
    /// </summary>
    public const string LanguageKo = "KO";

    /// <summary>
    /// Canonical language code for Brazilian Portuguese.
    /// </summary>
    public const string LanguagePtBr = "PT-BR";

    /// <summary>
    /// Canonical language code for Simplified Chinese.
    /// </summary>
    public const string LanguageZhCn = "ZH-CN";

    /// <summary>
    /// Canonical language code for Traditional Chinese.
    /// </summary>
    public const string LanguageZhTw = "ZH-TW";

    /// <summary>
    /// File name for Generals 1.08 authoritative CSV registry.
    /// </summary>
    public const string GeneralsCsvFileName = "Generals-1.08.csv";

    /// <summary>
    /// File name for Zero Hour 1.04 authoritative CSV registry.
    /// </summary>
    public const string ZeroHourCsvFileName = "ZeroHour-1.04.csv";

    /// <summary>
    /// File name for game installation files index JSON.
    /// </summary>
    public const string RegistryIndexFileName = "index.json";

    /// <summary>
    /// Documentation folder containing game installation files registry.
    /// </summary>
    public const string RegistryDocsFolder = "GameInstallationFilesRegistry";

    /// <summary>
    /// Embedded resource namespace in GenHub.Core containing authoritative registries.
    /// </summary>
    public const string EmbeddedResourceNamespace = "GenHub.Core.Assets.Registries";

    /// <summary>
    /// Environment variable name for overriding Generals CSV URL.
    /// </summary>
    public const string GeneralsCsvUrlEnvVar = "GENHUB_GENERALS_CSV_URL";

    /// <summary>
    /// Environment variable name for overriding Zero Hour CSV URL.
    /// </summary>
    public const string ZeroHourCsvUrlEnvVar = "GENHUB_ZEROHOUR_CSV_URL";

    /// <summary>
    /// Default remote URL for Generals 1.08 CSV.
    /// </summary>
    public const string DefaultGeneralsCsvUrl = "https://raw.githubusercontent.com/community-outpost/GenHub/main/docs/GameInstallationFilesRegistry/Generals-1.08.csv";

    /// <summary>
    /// Default remote URL for Zero Hour 1.04 CSV.
    /// </summary>
    public const string DefaultZeroHourCsvUrl = "https://raw.githubusercontent.com/community-outpost/GenHub/main/docs/GameInstallationFilesRegistry/ZeroHour-1.04.csv";

    /// <summary>
    /// Gets the remote URL for Generals 1.08 CSV, checking environment variable overrides first.
    /// </summary>
    public static string GeneralsCsvUrl =>
        Environment.GetEnvironmentVariable(GeneralsCsvUrlEnvVar) is { Length: > 0 } customUrl
            ? customUrl
            : DefaultGeneralsCsvUrl;

    /// <summary>
    /// Gets the remote URL for Zero Hour 1.04 CSV, checking environment variable overrides first.
    /// </summary>
    public static string ZeroHourCsvUrl =>
        Environment.GetEnvironmentVariable(ZeroHourCsvUrlEnvVar) is { Length: > 0 } customUrl
            ? customUrl
            : DefaultZeroHourCsvUrl;
}
