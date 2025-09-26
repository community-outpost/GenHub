using System;

namespace GenHub.Core.Constants;

/// <summary>Constants used for CSV file processing and content handling.</summary>
public static class CsvConstants
{
    /// <summary>CSV file extension.</summary>
    public const string CsvFileExtension = ".csv";

    /// <summary>CSV file pattern for file matching.</summary>
    public const string CsvFilePattern = "*.csv";

    /// <summary>Default CSV delimiter character.</summary>
    public const char CsvDelimiter = ',';

    /// <summary>Maximum number of columns expected in CSV files.</summary>
    public const int MaxCsvColumns = 8;

    /// <summary>Minimum number of columns required in CSV files.</summary>
    public const int MinCsvColumns = 4;

    /// <summary>Column index for relative path in CSV files (0-based).</summary>
    public const int RelativePathColumnIndex = 0;

    /// <summary>Column index for file size in CSV files (0-based).</summary>
    public const int SizeColumnIndex = 1;

    /// <summary>Column index for MD5 hash in CSV files (0-based).</summary>
    public const int Md5ColumnIndex = 2;

    /// <summary>Column index for SHA256 hash in CSV files (0-based).</summary>
    public const int Sha256ColumnIndex = 3;

    /// <summary>Column index for game type in CSV files (0-based).</summary>
    public const int GameTypeColumnIndex = 4;

    /// <summary>Column index for language in CSV files (0-based).</summary>
    public const int LanguageColumnIndex = 5;

    /// <summary>Column index for isRequired in CSV files (0-based).</summary>
    public const int IsRequiredColumnIndex = 6;

    /// <summary>Column index for metadata in CSV files (0-based).</summary>
    public const int MetadataColumnIndex = 7;

    /// <summary>Default buffer size for CSV file reading operations.</summary>
    public const int DefaultCsvBufferSize = 8192;

    /// <summary>Timeout for CSV download operations in seconds.</summary>
    public const int CsvDownloadTimeoutSeconds = 30;

    /// <summary>Maximum retry attempts for CSV operations.</summary>
    public const int MaxCsvRetryAttempts = 3;

    /// <summary>Delay between CSV retry attempts in milliseconds.</summary>
    public const int CsvRetryDelayMs = 1000;

    /// <summary>Default CSV URL for Generals content.</summary>
    public const string DefaultGeneralsCsvUrl = "https://raw.githubusercontent.com/Community-Outpost/GenHub/main/docs/GameInstallationFilesRegistry/Generals-1.08.csv";

    /// <summary>Default CSV URL for Zero Hour content.</summary>
    public const string DefaultZeroHourCsvUrl = "https://raw.githubusercontent.com/Community-Outpost/GenHub/main/docs/GameInstallationFilesRegistry/ZeroHour-1.04.csv";

    /// <summary>Default index.json URL for CSV registry metadata.</summary>
    public const string DefaultCsvIndexUrl = "https://raw.githubusercontent.com/Community-Outpost/GenHub/main/docs/GameInstallationFilesRegistry/index.json";

    /// <summary>CSV resolver identifier.</summary>
    public const string CsvResolverId = "CSVResolver";

    /// <summary>CSV source name identifier.</summary>
    public const string CsvSourceName = "CSV";

    /// <summary>File system deliverer source name.</summary>
    public const string FileSystemSourceName = "FileSystem";

    /// <summary>Default hash algorithm to use for file validation.</summary>
    public const string DefaultHashAlgorithm = "SHA256";

    /// <summary>Progress message for CSV download phase.</summary>
    public const string DownloadingCsvMessage = "Downloading CSV file...";

    /// <summary>Progress message for CSV parsing and validation phase.</summary>
    public const string ParsingCsvMessage = "Parsing CSV and validating files...";

    /// <summary>Error message when CSV URL is not provided.</summary>
    public const string CsvUrlNotProvidedError = "CSV URL not provided by discoverer";

    /// <summary>Error message when CSV download fails.</summary>
    public const string CsvDownloadFailedError = "Failed to download CSV";

    /// <summary>Error message when content is not found.</summary>
    public const string ContentNotFoundError = "Content not found";

    /// <summary>Error message when manifest is not available.</summary>
    public const string ManifestNotAvailableError = "Manifest not available in search result";

    /// <summary>Error message for operation cancellation.</summary>
    public const string OperationCancelledError = "Operation canceled";

    /// <summary>Error message for CSV resolve failure.</summary>
    public const string CsvResolveFailedError = "CSV resolve failed";

    /// <summary>Error message for CSV content preparation failure.</summary>
    public const string CsvPreparationFailedError = "CSV content preparation failed";

    /// <summary>Warning message for malformed CSV lines.</summary>
    public const string MalformedCsvLineWarning = "CSVDeliverer: skipping malformed CSV line {LineIndex} (not enough columns)";

    /// <summary>Warning message for empty relative paths.</summary>
    public const string EmptyRelativePathWarning = "CSVDeliverer: skipping empty relPath at line {LineIndex}";

    /// <summary>Warning message for size parse failures.</summary>
    public const string SizeParseFailedWarning = "CSVDeliverer: skipping {RelPath} because size parse failed at line {LineIndex}";

    /// <summary>Warning message for MD5 mismatches.</summary>
    public const string Md5MismatchWarning = "MD5 mismatch: {RelativePath}";

    /// <summary>Warning message for SHA256 mismatches.</summary>
    public const string Sha256MismatchWarning = "SHA256 mismatch: {RelativePath}";

    /// <summary>Warning message for missing files.</summary>
    public const string FileMissingWarning = "File missing: {RelativePath}";

    /// <summary>Warning message for invalid file sizes.</summary>
    public const string InvalidSizeWarning = "Invalid size for file {RelativePath}";

    /// <summary>Debug message for starting CSV preparation.</summary>
    public const string StartingCsvPreparationDebug = "Starting CSV content preparation for manifest {ManifestId}";

    /// <summary>Debug message for successful CSV preparation completion.</summary>
    public const string CsvPreparationCompletedDebug = "CSV content preparation completed successfully for manifest {ManifestId}";

    /// <summary>Warning message for CSV validation errors.</summary>
    public const string CsvValidationErrorsWarning = "CSV validation completed with errors: {Errors}";

    /// <summary>Error message for CSV download failures.</summary>
    public const string CsvDownloadFailedLog = "CSVDeliverer: failed to download CSV from {Url} - status {Status}";

    /// <summary>Error message for missing CSV URL.</summary>
    public const string CsvUrlMissingLog = "CSVDeliverer: csvUrl not provided in discovered item (Id={ContentId})";

    /// <summary>Warning message for operation cancellation.</summary>
    public const string OperationCancelledLog = "CSVDeliverer: operation was cancelled";

    /// <summary>Error message for CSV resolve failures.</summary>
    public const string CsvResolveFailedLog = "CSVDeliverer: failed to resolve CSV discovered item (Id={ContentId})";

    /// <summary>Error message for CSV preparation failures.</summary>
    public const string CsvPreparationFailedLog = "CSV content preparation failed for manifest {ManifestId}";
}
