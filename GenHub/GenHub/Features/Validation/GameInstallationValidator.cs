using GenHub.Core;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Validation;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameVersions;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Validation;

/// <summary>
/// Validates the integrity of a base game installation directory (e.g., from Steam, EA App).
/// </summary>
public class GameInstallationValidator : FileSystemValidator, IGameInstallationValidator
{
    private readonly ILogger<GameInstallationValidator> _logger;
    private readonly IManifestProvider _manifestProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameInstallationValidator"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="manifestProvider">The manifest provider.</param>
    public GameInstallationValidator(ILogger<GameInstallationValidator> logger, IManifestProvider manifestProvider)
        : base(logger ?? throw new ArgumentNullException(nameof(logger)))
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _manifestProvider = manifestProvider ?? throw new ArgumentNullException(nameof(manifestProvider));
    }

    /// <summary>
    /// Validates the specified game installation.
    /// </summary>
    /// <param name="installation">The game installation to validate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="ValidationResult"/> representing the validation outcome.</returns>
    public async Task<ValidationResult> ValidateAsync(GameInstallation installation, CancellationToken cancellationToken = default)
    {
        return await ValidateAsync(installation, null, cancellationToken);
    }

    /// <summary>
    /// Validates the specified game installation with progress reporting.
    /// </summary>
    /// <param name="installation">The game installation to validate.</param>
    /// <param name="progress">Progress reporter for MVVM integration.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="ValidationResult"/> representing the validation outcome.</returns>
    public async Task<ValidationResult> ValidateAsync(GameInstallation installation, IProgress<ValidationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Starting validation for installation '{Path}'", installation.InstallationPath);
        var issues = new List<ValidationIssue>();

        // Fetch manifest for this installation type
        var manifest = await _manifestProvider.GetManifestAsync(installation, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (manifest == null)
        {
            issues.Add(new ValidationIssue { IssueType = ValidationIssueType.MissingFile, Path = installation.InstallationPath, Message = "Manifest not found for installation." });
            return new ValidationResult(installation.InstallationPath, issues);
        }

        // Validate required directories
        issues.AddRange(await ValidateDirectoriesAsync(installation.InstallationPath, manifest.RequiredDirectories, cancellationToken));

        // Validate files (with progress reporting and path traversal security)
        issues.AddRange(await ValidateFilesAsync(installation.InstallationPath, manifest.Files, cancellationToken, progress));

        _logger.LogInformation("Installation validation for '{Path}' completed with {Count} issues.", installation.InstallationPath, issues.Count);
        return new ValidationResult(installation.InstallationPath, issues);
    }
}
