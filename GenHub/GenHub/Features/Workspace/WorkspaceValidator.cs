using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameVersions;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using GenHub.Core.Models.Workspace;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Workspace;

/// <summary>
/// Validates workspace configurations and system prerequisites.
/// </summary>
public class WorkspaceValidator(ILogger<WorkspaceValidator> logger) : IWorkspaceValidator
{
    private readonly ILogger<WorkspaceValidator> _logger = logger;

    /// <summary>
    /// Validates a workspace configuration.
    /// </summary>
    /// <param name="configuration">The configuration to validate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The validation result.</returns>
    public Task<ValidationResult> ValidateConfigurationAsync(WorkspaceConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();

        // Validate required properties
        if (string.IsNullOrWhiteSpace(configuration.Id))
        {
            issues.Add(new ValidationIssue
            {
                IssueType = ValidationIssueType.UnexpectedFile,
                Severity = ValidationSeverity.Error,
                Message = "Workspace ID is required",
                Path = nameof(configuration.Id),
            });
        }

        if (string.IsNullOrWhiteSpace(configuration.BaseInstallationPath))
        {
            issues.Add(new ValidationIssue
            {
                IssueType = ValidationIssueType.UnexpectedFile,
                Severity = ValidationSeverity.Error,
                Message = "Source installation path is required",
                Path = nameof(configuration.BaseInstallationPath),
            });
        }

        if (string.IsNullOrWhiteSpace(configuration.WorkspaceRootPath))
        {
            issues.Add(new ValidationIssue
            {
                IssueType = ValidationIssueType.UnexpectedFile,
                Severity = ValidationSeverity.Error,
                Message = "Workspace root path is required",
                Path = nameof(configuration.WorkspaceRootPath),
            });
        }

        // Validate paths exist
        if (!string.IsNullOrWhiteSpace(configuration.BaseInstallationPath) && !Directory.Exists(configuration.BaseInstallationPath))
        {
            issues.Add(new ValidationIssue
            {
                IssueType = ValidationIssueType.DirectoryMissing,
                Severity = ValidationSeverity.Error,
                Message = $"Source installation path does not exist: {configuration.BaseInstallationPath}",
                Path = configuration.BaseInstallationPath,
            });
        }

        // Validate workspace base path is writable
        if (!string.IsNullOrWhiteSpace(configuration.WorkspaceRootPath))
        {
            try
            {
                Directory.CreateDirectory(configuration.WorkspaceRootPath);
                var testFile = Path.Combine(configuration.WorkspaceRootPath, "test_write.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
            }
            catch (Exception ex)
            {
                issues.Add(new ValidationIssue
                {
                    IssueType = ValidationIssueType.DirectoryMissing,
                    Severity = ValidationSeverity.Error,
                    Message = $"Workspace root path is not writable: {ex.Message}",
                    Path = configuration.WorkspaceRootPath,
                });
            }
        }

        // Validate manifest has files
        if (configuration.Manifest?.Files == null || configuration.Manifest.Files.Count == 0)
        {
            issues.Add(new ValidationIssue
            {
                IssueType = ValidationIssueType.UnexpectedFile,
                Severity = ValidationSeverity.Error,
                Message = "Manifest must contain at least one file",
                Path = nameof(configuration.Manifest),
            });
        }

        return Task.FromResult(new ValidationResult(string.Empty, issues));
    }

    /// <summary>
    /// Validates system prerequisites for a workspace strategy.
    /// </summary>
    /// <param name="strategy">The workspace strategy to validate.</param>
    /// <param name="sourcePath">The source installation path.</param>
    /// <param name="destinationPath">The destination workspace path.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The validation result.</returns>
    public Task<ValidationResult> ValidatePrerequisitesAsync(IWorkspaceStrategy? strategy, string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();

        if (strategy != null)
        {
            // Use reflection to get properties if not on the interface
            var strategyType = WorkspaceStrategy.FullCopy;
            var requiresAdmin = false;
            var requiresSameVolume = false;
            var estimateMethod = strategy.GetType().GetMethod("EstimateDiskUsage");
            var propAdmin = strategy.GetType().GetProperty("RequiresAdminRights");
            var propSameVolume = strategy.GetType().GetProperty("RequiresSameVolume");
            var propStrategyType = strategy.GetType().GetProperty("StrategyType");
            if (propAdmin != null)
            {
                var val = propAdmin.GetValue(strategy);
                requiresAdmin = val is bool b && b;
            }

            if (propSameVolume != null)
            {
                var val = propSameVolume.GetValue(strategy);
                requiresSameVolume = val is bool b && b;
            }

            if (propStrategyType != null)
            {
                var val = propStrategyType.GetValue(strategy);
                if (val is WorkspaceStrategy sType)
                    strategyType = sType;
            }

            if (requiresAdmin)
            {
                if (!IsRunningAsAdministrator())
                {
                    issues.Add(new ValidationIssue
                    {
                        IssueType = ValidationIssueType.AccessDenied,
                        Severity = ValidationSeverity.Error,
                        Message = $"Strategy '{strategy.Name}' requires administrator privileges",
                        Path = "System",
                    });
                }
            }

            if (requiresSameVolume)
            {
                var sourceRoot = Path.GetPathRoot(sourcePath);
                var destRoot = Path.GetPathRoot(destinationPath);
                if (!string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new ValidationIssue
                    {
                        IssueType = ValidationIssueType.UnexpectedFile,
                        Severity = ValidationSeverity.Warning,
                        Message = $"Strategy '{strategy.Name ?? "Unknown"}' works best when source and destination are on the same volume. Source: {sourceRoot}, Destination: {destRoot}",
                        Path = "VolumeCheck",
                    });
                }
            }

            // Check available disk space
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(destinationPath) ?? destinationPath);
                long estimatedUsage = 0L;
                if (estimateMethod != null)
                {
                    var tempConfig = new WorkspaceConfiguration
                    {
                        Id = "temp-validation",
                        GameVersion = new GameVersion { Id = "temp" },
                        Manifest = new ContentManifest { Files = [] },
                        WorkspaceRootPath = Path.GetDirectoryName(destinationPath) ?? destinationPath,
                        BaseInstallationPath = sourcePath,
                        Strategy = (WorkspaceStrategy)strategyType,
                    };

                    var result = estimateMethod.Invoke(strategy, [tempConfig]);
                    estimatedUsage = result is long longValue ? longValue : 0L;
                }

                var safetyMargin = estimatedUsage * 0.1; // 10% safety margin
                if (drive.AvailableFreeSpace < estimatedUsage + safetyMargin)
                {
                    issues.Add(new ValidationIssue
                    {
                        IssueType = ValidationIssueType.InsufficientSpace,
                        Severity = ValidationSeverity.Warning,
                        Message = $"Low disk space. Available: {drive.AvailableFreeSpace / 1024 / 1024:N0} MB, Estimated needed: {estimatedUsage / 1024 / 1024:N0} MB (with safety margin)",
                        Path = destinationPath,
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not check disk space for {DestinationPath}", destinationPath);
            }
        }

        return Task.FromResult(new ValidationResult(string.Empty, issues));
    }

    private static bool IsRunningAsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
        {
            // On Unix systems, check if running as root
            return Environment.UserName == "root";
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
