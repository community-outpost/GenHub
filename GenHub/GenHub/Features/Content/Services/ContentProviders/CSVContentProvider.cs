using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.ContentProviders;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

// This class implemented in a very basic way and needs more work
// It needs to be split into smaller methods
// Also, the CSV format should be more robustly defined (e.g. using a library like CsvHelper)
// Error handling and logging should be improved
// It needs unit tests
// It is needed to implement caching for the downloaded CSV file
// And possibly for the validated manifests as well
// It needs more validation steps IValidator, contentValidation, etc

/// <summary>
/// CSV content provider that provides content from authoritative CSV catalogs.
/// Implements content discovery, resolution, and delivery for CSV-based content.
/// </summary>
/// <remarks>
/// This provider orchestrates the complete CSV content pipeline by coordinating
/// <see cref="IContentDiscoverer"/>, <see cref="IContentResolver"/>, and <see cref="IContentDeliverer"/>
/// components specifically designed for CSV-based content sources.
/// It inherits from <see cref="BaseContentProvider"/> to leverage common content provider functionality
/// while providing CSV-specific implementation details.
/// </remarks>
public class CsvContentProvider : BaseContentProvider
{
    private readonly IContentDiscoverer _discoverer;
    private readonly IContentResolver _resolver;
    private readonly IContentDeliverer _deliverer;
    private readonly ILogger<CsvContentProvider> _logger;
    private readonly string _csvUrl;

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvContentProvider"/> class.
    /// </summary>
    /// <param name="discoverers">The collection of available content discoverers.</param>
    /// <param name="resolvers">The collection of available content resolvers.</param>
    /// <param name="deliverers">The collection of available content deliverers.</param>
    /// <param name="logger">The logger for diagnostic information.</param>
    /// <param name="contentValidator">The content validator service.</param>
    /// <param name="csvUrl">The URL of the CSV file to process.</param>
    /// <remarks>
    /// This constructor sets up the CSV content provider by:
    /// <list type="number">
    /// <item>Finding the appropriate CSV <paramref name="discoverers"/> by source name</item>
    /// <item>Finding the appropriate CSV <paramref name="resolvers"/> by resolver ID</item>
    /// <item>Finding the appropriate <paramref name="deliverers"/> by source name</item>
    /// <item>Storing the <paramref name="csvUrl"/> for use during content preparation</item>
    /// </list>
    /// All parameters are required and the constructor will throw exceptions if required
    /// components cannot be found in the provided collections.
    /// </remarks>
    public CsvContentProvider(
        IEnumerable<IContentDiscoverer> discoverers,
        IEnumerable<IContentResolver> resolvers,
        IEnumerable<IContentDeliverer> deliverers,
        ILogger<CsvContentProvider> logger,
        IContentValidator contentValidator,
        string csvUrl)
        : base(contentValidator, logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _csvUrl = csvUrl ?? throw new ArgumentNullException(nameof(csvUrl));

        _discoverer = discoverers.FirstOrDefault(d => string.Equals(d.SourceName, CsvConstants.CsvSourceName, StringComparison.OrdinalIgnoreCase))
                      ?? throw new ArgumentException("CSV discoverer not found", nameof(discoverers));

        _resolver = resolvers.FirstOrDefault(r => string.Equals(r.ResolverId, CsvConstants.CsvResolverId, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException("CSV resolver not found", nameof(resolvers));

        _deliverer = deliverers.FirstOrDefault(d => string.Equals(d.SourceName, CsvConstants.FileSystemSourceName, StringComparison.OrdinalIgnoreCase))
                     ?? throw new ArgumentException("FileSystem deliverer not found", nameof(deliverers));
    }

    /// <inheritdoc />
    public override string SourceName => CsvConstants.CsvSourceName;

    /// <inheritdoc />
    public override string Description => "Content Provider backed by authoritative CSV catalog";

    /// <inheritdoc />
    public override bool IsEnabled => true;

    /// <inheritdoc />
    public override ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.RequiresDiscovery |
        ContentSourceCapabilities.SupportsManifestGeneration |
        ContentSourceCapabilities.LocalFileDelivery;

    /// <inheritdoc />
    protected override IContentDiscoverer Discoverer => _discoverer;

    /// <inheritdoc />
    protected override IContentResolver Resolver => _resolver;

    /// <inheritdoc />
    protected override IContentDeliverer Deliverer => _deliverer;

    /// <summary>
    /// Gets validated content by content ID from the CSV catalog.
    /// </summary>
    /// <param name="contentId">The unique identifier of the content.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="OperationResult{T}"/> containing the validated content manifest.</returns>
    /// <remarks>
    /// This method performs content lookup by ID through the CSV pipeline:
    /// <list type="number">
    /// <item>Creates a <see cref="ContentSearchQuery"/> with the content ID as search term</item>
    /// <item>Uses the inherited SearchAsync method to find matching content in the CSV catalog</item>
    /// <item>Extracts the <see cref="ContentManifest"/> from the first search result</item>
    /// <item>Returns success with the manifest or failure if not found</item>
    /// </list>
    /// The method supports cancellation and handles cases where content is not found gracefully.
    /// </remarks>
    public override async Task<OperationResult<ContentManifest>> GetValidatedContentAsync(
        string contentId,
        CancellationToken cancellationToken = default)
    {
        var query = new ContentSearchQuery
        {
            SearchTerm = contentId,
            Take = ContentConstants.SingleResultQueryLimit,
        };
        var searchResult = await SearchAsync(query, cancellationToken);
        if (!searchResult.Success || !searchResult.Data!.Any())
        {
            return OperationResult<ContentManifest>.CreateFailure($"{CsvConstants.ContentNotFoundError}: {contentId}");
        }

        var result = searchResult.Data!.First();
        var manifest = result.GetData<ContentManifest>();
        return manifest != null
            ? OperationResult<ContentManifest>.CreateSuccess(manifest)
            : OperationResult<ContentManifest>.CreateFailure(CsvConstants.ManifestNotAvailableError);
    }

    /// <summary>
    /// Prepares content internally by downloading and validating CSV data.
    /// </summary>
    /// <param name="manifest">The content manifest to prepare.</param>
    /// <param name="workingDirectory">The working directory for file operations.</param>
    /// <param name="progress">Optional progress reporter for the operation.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="OperationResult{T}"/> containing the prepared manifest.</returns>
    /// <remarks>
    /// This method implements the core CSV content preparation logic:
    /// <list type="number">
    /// <item>Downloads the CSV file from the configured URL using <see cref="HttpClient"/></item>
    /// <item>Parses the CSV content line by line, skipping the header row</item>
    /// <item>Validates each file entry by checking existence, size, MD5, and SHA256 hashes</item>
    /// <item>Creates <see cref="ManifestFile"/> objects for valid files and adds them to the manifest</item>
    /// <item>Reports progress through the <see cref="IProgress{T}"/> interface during download and validation phases</item>
    /// <item>Handles validation errors gracefully and returns detailed error messages</item>
    /// </list>
    /// The method uses cryptographic hashing (<see cref="MD5"/>, <see cref="SHA256"/>) to ensure file integrity
    /// and supports cancellation throughout the operation.
    /// </remarks>
    protected override async Task<OperationResult<ContentManifest>> PrepareContentInternalAsync(
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        try
        {
            _logger.LogDebug(CsvConstants.StartingCsvPreparationDebug, manifest.Id);
            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Downloading,
                CurrentOperation = CsvConstants.DownloadingCsvMessage,
            });

            // CSV download
            // Need refactoring and splitting into smaller methods
            string csvContent;
            using (var httpClient = new HttpClient())
            {
                csvContent = await httpClient.GetStringAsync(_csvUrl, cancellationToken);
            }

            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.ValidatingFiles,
                CurrentOperation = CsvConstants.ParsingCsvMessage,
            });

            var files = new List<ManifestFile>();

            using (var reader = new StringReader(csvContent))
            {
                string? line;
                bool isFirstLine = true;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (isFirstLine)
                    {
                        isFirstLine = false; // header
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var columns = line.Split(CsvConstants.CsvDelimiter);
                    if (columns.Length < CsvConstants.MinCsvColumns)
                    {
                        continue;
                    }

                    var relativePath = columns[CsvConstants.RelativePathColumnIndex].Trim();
                    var sizeString = columns[CsvConstants.SizeColumnIndex].Trim();
                    var md5 = columns[CsvConstants.Md5ColumnIndex].Trim();
                    var sha256 = columns[CsvConstants.Sha256ColumnIndex].Trim();

                    if (!long.TryParse(sizeString, out var size))
                    {
                        errors.Add(string.Format(CsvConstants.InvalidSizeWarning, relativePath));
                        continue;
                    }

                    var absolutePath = Path.Combine(workingDirectory, relativePath);
                    if (!File.Exists(absolutePath))
                    {
                        errors.Add(string.Format(CsvConstants.FileMissingWarning, relativePath));
                        continue;
                    }

                    var fileValid = true;

                    // Check MD5
                    using (var md5Algorithm = MD5.Create())
                    using (var stream = File.OpenRead(absolutePath))
                    {
                        var computedMd5 = BitConverter.ToString(md5Algorithm.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
                        if (!string.Equals(computedMd5, md5, StringComparison.OrdinalIgnoreCase))
                        {
                            errors.Add($"MD5 mismatch: {relativePath}");
                            fileValid = false;
                        }
                    }

                    // Check SHA256
                    using (var shaAlgorithm = SHA256.Create())
                    using (var stream = File.OpenRead(absolutePath))
                    {
                        var computedSha = BitConverter.ToString(shaAlgorithm.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
                        if (!string.Equals(computedSha, sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            errors.Add($"SHA256 mismatch: {relativePath}");
                            fileValid = false;
                        }
                    }

                    if (fileValid)
                    {
                        files.Add(new ManifestFile
                        {
                            RelativePath = relativePath,
                            Size = size,
                            Hash = md5,
                            SourceType = ContentSourceType.LocalFile,
                        });
                    }
                }
            }

            manifest.Files.Clear();
            manifest.Files.AddRange(files);

            // Validation
            // e.g var validationResult = await IValidate(manifest, cancellationToken);
            // Need more validation steps here
            if (errors.Any())
            {
                var errorMessage = string.Join("; ", errors);
                _logger.LogWarning("CSV validation completed with errors: {Errors}", errorMessage);
                return OperationResult<ContentManifest>.CreateFailure(errorMessage);
            }

            _logger.LogDebug("CSV content preparation completed successfully for manifest {ManifestId}", manifest.Id);
            return OperationResult<ContentManifest>.CreateSuccess(manifest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CSV content preparation failed for manifest {ManifestId}", manifest.Id);
            return OperationResult<ContentManifest>.CreateFailure($"CSV content preparation failed: {ex.Message}");
        }
    }
}
