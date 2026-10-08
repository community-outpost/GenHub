using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Validation;
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
        Assert.Equal(ValidationIssueType.InvalidManifest, issue.IssueType);
        Assert.Equal(0, result.MissingFilesCount);
    }

    /// <summary>Explicit null file collections must remain structural errors.</summary>
    /// <param name="withVariants">Whether valid platform variants are present.</param>
    /// <returns>The asynchronous test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidateManifestAsync_NullFiles_ReportsErrorAsync(bool withVariants)
    {
        var manifest = CreateManifest();
        if (!withVariants)
        {
            manifest.Variants.Clear();
        }

        manifest.Files = null!;
        var result = await _validator.ValidateManifestAsync(manifest);
        Assert.Contains(result.Issues, issue => issue.Message == "Manifest Files collection is null."
            && issue.Severity == ValidationSeverity.Error
            && issue.IssueType == ValidationIssueType.InvalidManifest);
        Assert.Equal(0, result.MissingFilesCount);
    }

    /// <summary>Malformed paths produce validation issues rather than aborting validation.</summary>
    /// <param name="path">The invalid relative path.</param>
    /// <returns>The asynchronous test.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ValidateAllAsync_MissingRelativePath_ReturnsErrorAsync(string? path)
    {
        var manifest = CreateManifest();
        ManifestVariantResolver.ResolveVariant(manifest)!.Files[0].RelativePath = path!;
        var result = await _validator.ValidateAllAsync(_contentDirectory, manifest);
        Assert.Contains(result.Issues, issue => issue.Message == "Manifest file is missing its RelativePath."
            && issue.Severity == ValidationSeverity.Error
            && issue.IssueType == ValidationIssueType.InvalidManifest);
        Assert.DoesNotContain(result.Issues, issue => issue.Message.Contains("Unexpected error", StringComparison.Ordinal));
    }

    /// <summary>Unsupported variants cannot pass host integrity validation as an empty manifest.</summary>
    /// <param name="fullValidation">Whether all validation phases are run.</param>
    /// <returns>The asynchronous test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidateIntegrity_UnsupportedRuntime_ReturnsErrorAsync(bool fullValidation)
    {
        var manifest = CreateManifest();
        foreach (var variant in manifest.Variants)
        {
            variant.RuntimeIdentifiers = ["unsupported-runtime"];
        }

        var result = fullValidation
            ? await _validator.ValidateAllAsync(_contentDirectory, manifest)
            : await _validator.ValidateContentIntegrityAsync(_contentDirectory, manifest);
        Assert.Contains(result.Issues, issue => issue.Message.Contains("no variant supporting this host", StringComparison.Ordinal)
            && issue.Severity == ValidationSeverity.Error
            && issue.IssueType == ValidationIssueType.ValidationUnavailable);
        Assert.Equal(0, result.TotalFilesValidated);
        Assert.Equal(0, result.MissingFilesCount);
    }

    /// <summary>Without a host variant, files on disk are not reported as unexpected.</summary>
    /// <param name="fullValidation">Whether all validation phases are run.</param>
    /// <returns>The asynchronous test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DetectExtraneous_UnsupportedRuntime_ReportsOnlyHostErrorAsync(bool fullValidation)
    {
        await File.WriteAllTextAsync(Path.Combine(_contentDirectory, ForeignFileName), "foreign");
        var manifest = CreateManifest();
        manifest.Variants.RemoveAt(1);

        var result = fullValidation
            ? await _validator.ValidateAllAsync(_contentDirectory, manifest)
            : await _validator.DetectExtraneousFilesAsync(_contentDirectory, manifest);

        Assert.DoesNotContain(result.Issues, issue => issue.IssueType == ValidationIssueType.UnexpectedFile);
        Assert.Single(result.Issues, issue => issue.Message.Contains("no variant supporting this host", StringComparison.Ordinal));
    }

    /// <summary>A null variant entry is reported as a structural error instead of throwing.</summary>
    /// <param name="fullValidation">Whether all validation phases are run.</param>
    /// <returns>The asynchronous test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Validate_NullVariant_ReportsErrorAsync(bool fullValidation)
    {
        var manifest = CreateManifest();
        manifest.Variants.Insert(0, null!);

        var result = fullValidation
            ? await _validator.ValidateAllAsync(_contentDirectory, manifest)
            : await _validator.ValidateContentIntegrityAsync(_contentDirectory, manifest);

        Assert.Equal(1, result.TotalFilesValidated);
        if (fullValidation)
        {
            Assert.Contains(result.Issues, issue => issue.Message == "Variant at index 0 is null."
                && issue.Severity == ValidationSeverity.Error);
        }
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
        catch (IOException)
        {
            // Ignore cleanup errors
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore cleanup errors
        }
    }

    /// <summary>
    /// A null file entry is skipped by integrity validation and reported by the structural check,
    /// so full validation still returns the error instead of throwing.
    /// </summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task ValidateAllAsync_NullFile_ReportsStructuralErrorAsync()
    {
        var manifest = CreateManifest();
        ManifestVariantResolver.ResolveVariant(manifest)!.Files.Insert(0, null!);

        var integrity = await _validator.ValidateContentIntegrityAsync(_contentDirectory, manifest);
        var result = await _validator.ValidateAllAsync(_contentDirectory, manifest);

        Assert.Equal(1, integrity.TotalFilesValidated);
        Assert.Contains(result.Issues, issue => issue.Message == "File at index 0 in variant 1 is null."
            && issue.Severity == ValidationSeverity.Error
            && issue.IssueType == ValidationIssueType.InvalidManifest);
    }

    /// <summary>Root entries are validated even when runtime variants supply the payload.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task ValidateManifestAsync_VariantsWithNullRootFile_ReportsErrorAsync()
    {
        var manifest = CreateManifest();
        manifest.Files = [null!];
        var result = await _validator.ValidateManifestAsync(manifest);
        Assert.Contains(result.Issues, issue => issue.Message.Contains("File at index 0 is null"));
    }

    private static ContentManifest CreateManifest() => VariantManifestFixture.Create(
        [new() { RelativePath = HostFileName, SourceType = ContentSourceType.GameInstallation }],
        [new() { RelativePath = ForeignFileName, SourceType = ContentSourceType.GameInstallation }]);
}
