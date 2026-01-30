namespace GenHub.Core.Constants;

/// <summary>
/// Constants specific to CSV catalog discovery and content pipeline.
/// </summary>
public static class CsvConstants
{
    /// <summary>
    /// Source name for the CSV catalog discoverer.
    /// </summary>
    public const string SourceName = "Csv Discoverer";

    /// <summary>
    /// Description for the CSV catalog discoverer.
    /// </summary>
    public const string Description = "Discovers base game manifests from verified CSV catalogs.";

    /// <summary>
    /// Publisher ID for the CSV registry service.
    /// </summary>
    public const string PublisherId = "csvregistry";

    /// <summary>
    /// Resolver ID for CSV catalog content.
    /// </summary>
    public const string ResolverId = "CsvCatalogResolver";

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
    public const string AllLanguagesFilter = "ALL";
}
