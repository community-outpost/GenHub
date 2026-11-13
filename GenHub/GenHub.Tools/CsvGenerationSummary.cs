namespace GenHub.Tools;

/// <summary>
/// Summary results of a CSV generation execution.
/// </summary>
/// <param name="TotalFilesScanned">The total number of files scanned in the installation directory.</param>
/// <param name="TotalEntriesWritten">The total number of entries written to the CSV file.</param>
/// <param name="TotalSizeBytes">The total size of the generated CSV file in bytes.</param>
/// <param name="CsvPath">The absolute path to the generated CSV file.</param>
/// <param name="CsvMd5">The MD5 hash of the generated CSV file.</param>
/// <param name="CsvSha256">The SHA256 hash of the generated CSV file.</param>
/// <param name="IndexUpdated">Whether the index.json was successfully updated.</param>
public sealed record CsvGenerationSummary(
    int TotalFilesScanned,
    int TotalEntriesWritten,
    long TotalSizeBytes,
    string CsvPath,
    string CsvMd5,
    string CsvSha256,
    bool IndexUpdated);
