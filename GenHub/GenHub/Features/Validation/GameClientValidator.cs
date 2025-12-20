using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Validation;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Validation;

/// <summary>
/// Validates the integrity of a specific game client workspace using manifest-driven checks.
/// </summary>
/// <param name="logger">Logger instance.</param>
/// <param name="manifestProvider">Manifest provider.</param>
/// <param name="contentValidator">Content validator for core validation logic.</param>
/// <param name="hashProvider">File hash provider for file system validation.</param>
public class GameClientValidator(
    ILogger<GameClientValidator> logger,
    IManifestProvider manifestProvider,
    IContentValidator contentValidator,
    IFileHashProvider hashProvider)
    : FileSystemValidator(logger ?? throw new ArgumentNullException(nameof(logger)), hashProvider ?? throw new ArgumentNullException(nameof(hashProvider))), IGameClientValidator, IValidator<GameClient>
{
    private readonly ILogger<GameClientValidator> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IManifestProvider _manifestProvider = manifestProvider ?? throw new ArgumentNullException(nameof(manifestProvider));
    private readonly IContentValidator _contentValidator = contentValidator ?? throw new ArgumentNullException(nameof(contentValidator));
    private readonly IFileHashProvider _hashProvider = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));

    /// <inheritdoc/>
    public async Task<ValidationResult> ValidateAsync(GameClient gameClient, CancellationToken cancellationToken = default)
    {
        return await ValidateAsync(gameClient, null, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ValidationResult> ValidateAsync(GameClient gameClient, IProgress<ValidationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Starting validation for client '{ClientName}' (ID: {ClientId}) at '{Path}'", gameClient.Name, gameClient.Id, gameClient.WorkingDirectory);
        var issues = new List<ValidationIssue>();

        // Early validation - check if working directory exists
        if (string.IsNullOrEmpty(gameClient.WorkingDirectory) || !Directory.Exists(gameClient.WorkingDirectory))
        {
            issues.Add(new ValidationIssue { IssueType = ValidationIssueType.DirectoryMissing, Path = gameClient.WorkingDirectory, Message = "Game client working directory is missing or not prepared." });
            _logger.LogError("Validation failed: Working directory '{Path}' is invalid.", gameClient.WorkingDirectory);
            return new ValidationResult(gameClient.Id, issues);
        }

        progress?.Report(new ValidationProgress(1, 4, "Fetching manifest"));

        // Get manifest
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = await _manifestProvider.GetManifestAsync(gameClient, cancellationToken);
        if (manifest == null)
        {
            issues.Add(new ValidationIssue { IssueType = ValidationIssueType.MissingFile, Path = "Manifest", Message = "Validation manifest could not be found for this game client." });
            _logger.LogError("Validation failed: No manifest found for game client ID '{ClientId}'.", gameClient.Id);
            return new ValidationResult(gameClient.Id, issues);
        }

        progress?.Report(new ValidationProgress(2, 4, "Core manifest validation"));

        // Use ContentValidator for core validation
        var manifestValidationResult = await _contentValidator.ValidateManifestAsync(manifest, cancellationToken);
        issues.AddRange(manifestValidationResult.Issues);

        progress?.Report(new ValidationProgress(3, 4, "Content integrity validation"));

        // Use ContentValidator for file integrity
        var integrityValidationResult = await _contentValidator.ValidateContentIntegrityAsync(gameClient.WorkingDirectory, manifest, cancellationToken);
        issues.AddRange(integrityValidationResult.Issues);

        progress?.Report(new ValidationProgress(4, 4, "Game client specific checks"));

        // Game client specific validations
        issues.AddRange(await ValidateGameClientSpecificAsync(gameClient, manifest, cancellationToken));

        _logger.LogInformation("Validation for '{ClientName}' completed with {IssueCount} issues.", gameClient.Name, issues.Count);
        return new ValidationResult(gameClient.Id, issues);
    }

    private static async Task<List<ValidationIssue>> ValidateGameClientSpecificAsync(GameClient gameClient, ContentManifest manifest, CancellationToken cancellationToken)
    {
        var issues = new List<ValidationIssue>();

        // Get actual files for known addon detection
        var actualFiles = await Task.Run(
                () =>
            Directory.EnumerateFiles(gameClient.WorkingDirectory, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(gameClient.WorkingDirectory, f).Replace('\\', '/'))
                .ToList(),
                cancellationToken);

        // KnownAddons detection - check against actual files
        if (manifest.KnownAddons != null)
        {
            foreach (var knownAddon in manifest.KnownAddons)
            {
                foreach (var actualFile in actualFiles)
                {
                    if (!string.IsNullOrEmpty(knownAddon) && actualFile.Contains(knownAddon, StringComparison.OrdinalIgnoreCase))
                    {
                        issues.Add(new ValidationIssue
                        {
                            IssueType = ValidationIssueType.AddonDetected,
                            Path = actualFile,
                            Message = $"Detected known addon: {knownAddon}",
                            Severity = ValidationSeverity.Warning,
                        });
                    }
                }
            }
        }

        // Unexpected file detection
        var expectedRelativePaths = manifest.Files.Select(f => f.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var actualRelativePath in actualFiles)
        {
            if (!expectedRelativePaths.Contains(actualRelativePath))
            {
                issues.Add(new ValidationIssue { IssueType = ValidationIssueType.UnexpectedFile, Path = actualRelativePath, Message = "An unexpected file was found in the working directory.", Severity = ValidationSeverity.Warning });
            }
        }

        return issues;
    }
}
