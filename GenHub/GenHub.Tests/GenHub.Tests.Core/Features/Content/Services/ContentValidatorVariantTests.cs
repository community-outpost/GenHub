using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services;
using GenHub.Tests.Core.Models.Manifest;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Tests that <see cref="ContentValidator"/> validates the host variant's files of manifests
/// whose files live in platform variants rather than the flat list.
/// </summary>
public sealed class ContentValidatorVariantTests : IDisposable
{
    private const string HostFileName = "generalszh-host";
    private const string ForeignFileName = "generalszh-foreign.exe";

    private readonly string _contentDirectory = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString("N"));
    private readonly ContentValidator _validator = new(
        new Mock<IFileOperationsService>().Object,
        new Mock<ICasService>().Object,
        new Mock<ILogger<ContentValidator>>().Object);

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentValidatorVariantTests"/> class.
    /// </summary>
    public ContentValidatorVariantTests()
    {
        Directory.CreateDirectory(_contentDirectory);
    }

    /// <summary>
    /// Integrity validation checks the host variant's files and ignores the foreign variant's.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ValidateContentIntegrityAsync_WithVariantManifest_ChecksOnlyHostVariantAsync()
    {
        var result = await _validator.ValidateContentIntegrityAsync(_contentDirectory, CreateManifest());

        Assert.Equal(1, result.TotalFilesValidated);
        var issue = Assert.Single(result.Issues);
        Assert.Contains(HostFileName, issue.Message);
    }

    /// <summary>
    /// Extraneous-file detection expects the host variant's files.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DetectExtraneousFilesAsync_WithVariantManifest_ExpectsHostVariantFilesAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(_contentDirectory, HostFileName), "host");

        var result = await _validator.DetectExtraneousFilesAsync(_contentDirectory, CreateManifest());

        Assert.DoesNotContain(result.Issues, issue => issue.Message.Contains(HostFileName, StringComparison.Ordinal));
    }

    /// <summary>
    /// Structural validation counts the files declared in variants, so a variant manifest
    /// with an empty flat list is not reported as having no files.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ValidateManifestAsync_WithVariantManifest_DoesNotReportNoFilesAsync()
    {
        var result = await _validator.ValidateManifestAsync(CreateManifest());

        Assert.DoesNotContain(result.Issues, issue => issue.Message.Contains("no files", StringComparison.Ordinal));
    }

    /// <summary>
    /// Structural validation reports a file with no relative path in any variant, and names
    /// its position within that variant.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ValidateManifestAsync_WithMissingPathInForeignVariant_ReportsItsLocationAsync()
    {
        var manifest = VariantManifestFixture.Create(
            [new() { RelativePath = HostFileName, SourceType = ContentSourceType.GameInstallation }],
            [
                new() { RelativePath = ForeignFileName, SourceType = ContentSourceType.GameInstallation },
                new() { RelativePath = string.Empty, SourceType = ContentSourceType.GameInstallation },
            ]);

        var result = await _validator.ValidateManifestAsync(manifest);

        var issue = Assert.Single(result.Issues, issue => issue.Message.Contains("RelativePath", StringComparison.Ordinal));
        Assert.Equal("File at index 1 in variant 0 is missing its RelativePath.", issue.Message);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_contentDirectory))
            {
                Directory.Delete(_contentDirectory, true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore cleanup errors
        }
    }

    private static ContentManifest CreateManifest() => VariantManifestFixture.Create(
        [new() { RelativePath = HostFileName, SourceType = ContentSourceType.GameInstallation }],
        [new() { RelativePath = ForeignFileName, SourceType = ContentSourceType.GameInstallation }]);
}
