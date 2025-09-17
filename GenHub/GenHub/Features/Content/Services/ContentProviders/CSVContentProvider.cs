using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Enums;
using GenHub.Features.Content.Services.ContentProviders;
using GenHub.Core.Constants;
// this class implemnted in a very basic way and needs more work
// it needs to be split into smaller methods
// also, the CSV format should be more robustly defined (e.g. using a library like CsvHelper)
// error handling and logging should be improved
// it needs unit tests
// it is needed to implement caching for the downloaded CSV file
// and possibly for the validated manifests as well
// it needs more validation steps IValdator, contentValidation, etc

namespace GenHub.Features.Content.Services.ContentProviders
{
    public class CsvContentProvider : BaseContentProvider
    {
        private readonly IContentDiscoverer _discoverer;
        private readonly IContentResolver _resolver;
        private readonly IContentDeliverer _deliverer;
        private readonly ILogger<CsvContentProvider> _logger;
        private readonly string _csvUrl;

        public CsvContentProvider(
            IEnumerable<IContentDiscoverer> discoverers,
            IEnumerable<IContentResolver> resolvers,
            IEnumerable<IContentDeliverer> deliverers,
            ILogger<CsvContentProvider> logger,
            IContentValidator contentValidator,
            string csvUrl
            ) : base(contentValidator, logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _csvUrl = csvUrl ?? throw new ArgumentNullException(nameof(csvUrl));

            _discoverer = discoverers.FirstOrDefault(d => string.Equals(d.SourceName, "CSV", StringComparison.OrdinalIgnoreCase))
                          ?? throw new ArgumentException("CSV discoverer not found", nameof(discoverers));

            _resolver = resolvers.FirstOrDefault(r => string.Equals(r.ResolverId, "CsvResolver", StringComparison.OrdinalIgnoreCase))
                        ?? throw new ArgumentException("CSV resolver not found", nameof(resolvers));

            _deliverer = deliverers.FirstOrDefault(d => string.Equals(d.SourceName, "FileSystem", StringComparison.OrdinalIgnoreCase))
                         ?? throw new ArgumentException("FileSystem deliverer not found", nameof(deliverers));
        }

        public override string SourceName => "CSV";
        public override string Description => "Content Provider backed by authoritative CSV catalog";
        public override bool IsEnabled => true;

        public override ContentSourceCapabilities Capabilities =>
            ContentSourceCapabilities.RequiresDiscovery |
            ContentSourceCapabilities.SupportsManifestGeneration |
            ContentSourceCapabilities.LocalFileDelivery;

         protected override IContentDiscoverer Discoverer => _discoverer;
        protected override IContentResolver Resolver => _resolver;
        protected override IContentDeliverer Deliverer => _deliverer;

        public override async Task<OperationResult<ContentManifest>> GetValidatedContentAsync(
            string contentId, CancellationToken cancellationToken = default)
        {
            var query = new ContentSearchQuery { SearchTerm = contentId, Take = ContentConstants.SingleResultQueryLimit };
            var searchResult = await SearchAsync(query, cancellationToken);
            if (!searchResult.Success || !searchResult.Data!.Any())
            {
                return OperationResult<ContentManifest>.CreateFailure($"Content not found: {contentId}");
            }

            var result = searchResult.Data!.First();
            var manifest = result.GetData<ContentManifest>();
            return manifest != null
                ? OperationResult<ContentManifest>.CreateSuccess(manifest)
                : OperationResult<ContentManifest>.CreateFailure("Manifest not available in search result");
        }

        protected override async Task<OperationResult<ContentManifest>> PrepareContentInternalAsync(
            ContentManifest manifest,
            string workingDirectory,
            IProgress<ContentAcquisitionProgress>? progress,
            CancellationToken cancellationToken)
        {
            var errors = new List<string>();

            try
            {
                _logger.LogDebug("Starting CSV content preparation for manifest {ManifestId}", manifest.Id);
                progress?.Report(new ContentAcquisitionProgress
                {
                    Phase = ContentAcquisitionPhase.Downloading,
                    CurrentOperation = "Downloading CSV file..."
                });

                // csv download
                // 
                // need refactoring and splitting into smaller methods;
                string csvContent;
                using (var httpClient = new HttpClient())
                {
                    csvContent = await httpClient.GetStringAsync(_csvUrl, cancellationToken);
                }

                progress?.Report(new ContentAcquisitionProgress
                {
                    Phase = ContentAcquisitionPhase.ValidatingFiles,
                    CurrentOperation = "Parsing CSV and validating files..."
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

                        if (string.IsNullOrWhiteSpace(line)) continue;

                        var columns = line.Split(',');
                        if (columns.Length < 4) continue;

                        var relPath = columns[0].Trim();
                        var sizeStr = columns[1].Trim();
                        var md5 = columns[2].Trim();
                        var sha256 = columns[3].Trim();

                        if (!long.TryParse(sizeStr, out var size))
                        {
                            errors.Add($"Invalid size for file {relPath}");
                            continue;
                        }

                        var absolutePath = Path.Combine(workingDirectory, relPath);
                        if (!File.Exists(absolutePath))
                        {
                            errors.Add($"File missing: {relPath}");
                            continue;
                        }

                        var fileValid = true;

                        // check MD5
                        using (var md5Alg = MD5.Create())
                        using (var stream = File.OpenRead(absolutePath))
                        {
                            var computedMd5 = BitConverter.ToString(md5Alg.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                            if (!string.Equals(computedMd5, md5, StringComparison.OrdinalIgnoreCase))
                            {
                                errors.Add($"MD5 mismatch: {relPath}");
                                fileValid = false;
                            }
                        }

                        // check SHA256
                        using (var shaAlg = SHA256.Create())
                        using (var stream = File.OpenRead(absolutePath))
                        {
                            var computedSha = BitConverter.ToString(shaAlg.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                            if (!string.Equals(computedSha, sha256, StringComparison.OrdinalIgnoreCase))
                            {
                                errors.Add($"SHA256 mismatch: {relPath}");
                                fileValid = false;
                            }
                        }

                        if (fileValid)
                        {
                            files.Add(new ManifestFile
                            {
                                RelativePath = relPath,
                                Size = size,
                                Hash = md5,
                                SourceType = ContentSourceType.LocalFile
                            });
                        }
                    }
                }
                //160
                manifest.Files.Clear();
                manifest.Files.AddRange(files);
                // validation
                // e.g var validationResult = await Ivaldate(manifest, cancellationToken);

                // need more validation steps here
               
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
}