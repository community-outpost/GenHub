using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Service for validating and executing manifest-declared installation steps.
/// Enforces trust boundaries, path containment, and hash verification before execution.
/// </summary>
/// <param name="hashProvider">The file hash provider for integrity verification.</param>
/// <param name="notificationService">The notification service for user awareness.</param>
/// <param name="userSettingsService">The user settings service for tracking executed installation steps across updates.</param>
/// <param name="preconditions">Optional installation step preconditions for environment detection.</param>
/// <param name="logger">The logger instance.</param>
public class InstallationInstructionsService(
    IFileHashProvider hashProvider,
    INotificationService notificationService,
    IUserSettingsService? userSettingsService,
    IEnumerable<IInstallationStepPrecondition>? preconditions,
    ILogger<InstallationInstructionsService> logger) : IInstallationInstructionsService
{
    private static readonly TimeSpan InstallerStepTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Initializes a new instance of the <see cref="InstallationInstructionsService"/> class.
    /// </summary>
    /// <param name="hashProvider">The file hash provider for integrity verification.</param>
    /// <param name="notificationService">The notification service for user awareness.</param>
    /// <param name="userSettingsService">The user settings service for tracking executed installation steps across updates.</param>
    /// <param name="logger">The logger instance.</param>
    public InstallationInstructionsService(
        IFileHashProvider hashProvider,
        INotificationService notificationService,
        IUserSettingsService? userSettingsService,
        ILogger<InstallationInstructionsService> logger)
        : this(hashProvider, notificationService, userSettingsService, null, logger)
    {
    }

    /// <inheritdoc />
    public Task<OperationResult> ExecutePreInstallStepsAsync(
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return ExecutePreInstallStepsAsync(manifest, workingDirectory, force: false, progress, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult> ExecutePreInstallStepsAsync(
        ContentManifest manifest,
        string workingDirectory,
        bool force,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.InstallationInstructions?.PreInstallSteps == null ||
            manifest.InstallationInstructions.PreInstallSteps.Count == 0)
        {
            return OperationResult.CreateSuccess();
        }

        logger.LogInformation(
            "Executing {Count} pre-install step(s) for manifest {ManifestId} (force: {Force})",
            manifest.InstallationInstructions.PreInstallSteps.Count,
            manifest.Id,
            force);

        return await ExecuteStepsAsync(
            manifest.InstallationInstructions.PreInstallSteps,
            manifest,
            workingDirectory,
            force,
            progress,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult> ExecutePostInstallStepsAsync(
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return ExecutePostInstallStepsAsync(manifest, workingDirectory, force: false, progress, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult> ExecutePostInstallStepsAsync(
        ContentManifest manifest,
        string workingDirectory,
        bool force,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.InstallationInstructions?.PostInstallSteps == null ||
            manifest.InstallationInstructions.PostInstallSteps.Count == 0)
        {
            return OperationResult.CreateSuccess();
        }

        logger.LogInformation(
            "Executing {Count} post-install step(s) for manifest {ManifestId} (force: {Force})",
            manifest.InstallationInstructions.PostInstallSteps.Count,
            manifest.Id,
            force);

        return await ExecuteStepsAsync(
            manifest.InstallationInstructions.PostInstallSteps,
            manifest,
            workingDirectory,
            force,
            progress,
            cancellationToken);
    }

    private async Task<OperationResult> ExecuteStepsAsync(
        IReadOnlyList<InstallationStep> steps,
        ContentManifest manifest,
        string workingDirectory,
        bool force,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            return OperationResult.CreateFailure($"Working directory does not exist: '{workingDirectory}'");
        }

        for (var i = 0; i < steps.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = steps[i];

            if (step == null)
            {
                continue;
            }

            var stepResult = await ExecuteSingleStepAsync(step, manifest, workingDirectory, force, progress, cancellationToken);
            if (!stepResult.Success)
            {
                return stepResult;
            }
        }

        return OperationResult.CreateSuccess();
    }

    private async Task<OperationResult> ExecuteSingleStepAsync(
        InstallationStep step,
        ContentManifest manifest,
        string workingDirectory,
        bool force,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stepKey = GetStepKey(step, manifest);

        if (!force && step.RunOnce && await ShouldSkipStepAsync(step, stepKey, manifest, cancellationToken))
        {
            logger.LogInformation(
                "Skipping installation step '{StepName}' for manifest {ManifestId} because it has already been executed (key: {StepKey})",
                step.Name,
                manifest.Id,
                stepKey);

            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Extracting,
                CurrentOperation = $"Skipping {step.Name} (already installed)",
                CurrentFile = step.TargetRelativePath ?? string.Empty,
            });

            return OperationResult.CreateSuccess();
        }

        var result = OperationResult.CreateFailure("Uninitialized step result");
        switch (step.Kind)
        {
            case InstallationStepKind.RunVerifiedInstaller:
                result = await ExecuteRunVerifiedInstallerAsync(step, manifest, workingDirectory, progress, cancellationToken);
                break;

            case InstallationStepKind.RemoveFile:
                result = ExecuteRemoveFile(step, workingDirectory);
                break;

            case InstallationStepKind.RenameFile:
                result = ExecuteRenameFile(step, workingDirectory);
                break;

            default:
                logger.LogError("Unsupported installation step kind '{Kind}' in step '{StepName}'", step.Kind, step.Name);
                return OperationResult.CreateFailure($"Unsupported installation step kind '{step.Kind}' for step '{step.Name}'.");
        }

        if (result.Success && step.RunOnce && userSettingsService != null && !string.IsNullOrWhiteSpace(stepKey))
        {
            userSettingsService.Update(s => s.RecordInstallationStepExecuted(stepKey));
            try
            {
                await userSettingsService.SaveAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to persist executed installation step key '{StepKey}'", stepKey);
            }
        }

        return result;
    }

    private async Task<bool> ShouldSkipStepAsync(
        InstallationStep step,
        string stepKey,
        ContentManifest manifest,
        CancellationToken cancellationToken)
    {
        if (userSettingsService?.Get().IsInstallationStepExecuted(stepKey) == true)
        {
            return true;
        }

        if (preconditions != null)
        {
            foreach (var precondition in preconditions)
            {
                if (precondition.CanHandle(step, manifest) && precondition.IsAlreadyFulfilled(step, manifest))
                {
                    if (userSettingsService != null && !string.IsNullOrWhiteSpace(stepKey))
                    {
                        userSettingsService.Update(s => s.RecordInstallationStepExecuted(stepKey));
                        try
                        {
                            await userSettingsService.SaveAsync(cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Failed to persist detected installation step key '{StepKey}'", stepKey);
                        }
                    }

                    return true;
                }
            }
        }

        return false;
    }

    private string GetStepKey(InstallationStep step, ContentManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(step.StepKey))
        {
            return step.StepKey;
        }

        var publisher = manifest.Publisher?.PublisherType ?? "generic";
        var name = step.Name;
        var target = step.TargetRelativePath ?? string.Empty;
        var args = step.Arguments is { Count: > 0 } ? string.Join(" ", step.Arguments) : string.Empty;

        return $"{publisher}:{name}:{target}:{args}".TrimEnd(':');
    }

    private async Task<OperationResult> ExecuteRunVerifiedInstallerAsync(
        InstallationStep step,
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        // 1. Publisher authorization check
        var publisherType = manifest.Publisher?.PublisherType ?? string.Empty;
        var publisherName = manifest.Publisher?.Name ?? string.Empty;

        var isTrusted = PublisherTypeConstants.TrustedExecutablePublishers.Contains(publisherType) ||
                        PublisherTypeConstants.TrustedExecutablePublishers.Contains(publisherName);

        if (!isTrusted)
        {
            logger.LogError(
                "Untrusted publisher '{PublisherType}' ({PublisherName}) attempted to execute installer step '{StepName}' for manifest {ManifestId}",
                publisherType,
                publisherName,
                step.Name,
                manifest.Id);

            return OperationResult.CreateFailure(
                $"Publisher '{(!string.IsNullOrEmpty(publisherType) ? publisherType : publisherName)}' is not authorized to execute installation steps.");
        }

        // 2. Target path validation
        if (string.IsNullOrWhiteSpace(step.TargetRelativePath))
        {
            return OperationResult.CreateFailure($"Target relative path is required for executable step '{step.Name}'.");
        }

        var normalizedRelativePath = PathHelper.NormalizeRelativePath(step.TargetRelativePath);
        var targetFullPath = Path.Combine(workingDirectory, normalizedRelativePath);

        if (!PathHelper.IsPathContainedIn(targetFullPath, workingDirectory))
        {
            logger.LogError("Target installer path '{Target}' escapes working directory '{Dir}'", step.TargetRelativePath, workingDirectory);
            return OperationResult.CreateFailure($"Installer path '{step.TargetRelativePath}' escapes the working directory.");
        }

        if (!File.Exists(targetFullPath))
        {
            logger.LogError("Installer executable not found at '{Path}'", targetFullPath);
            return OperationResult.CreateFailure($"Installer executable '{step.TargetRelativePath}' was not found in delivered content.");
        }

        // 3. Manifest file declaration and integrity verification
        var manifestFile = manifest.Files?.FirstOrDefault(f =>
            string.Equals(
                PathHelper.NormalizeRelativePath(f.RelativePath),
                normalizedRelativePath,
                PathHelper.PathComparison));

        if (manifestFile == null)
        {
            logger.LogError("Executable '{Target}' is not declared in manifest files for {ManifestId}", step.TargetRelativePath, manifest.Id);
            return OperationResult.CreateFailure($"Installer executable '{step.TargetRelativePath}' is not declared in manifest files.");
        }

        if (string.IsNullOrWhiteSpace(manifestFile.Hash))
        {
            logger.LogError("Installer '{Target}' has no declared hash in manifest {ManifestId}", step.TargetRelativePath, manifest.Id);
            return OperationResult.CreateFailure(
                $"Installer '{step.TargetRelativePath}' has no declared hash and cannot be verified.");
        }

        var computedHash = await hashProvider.ComputeFileHashAsync(targetFullPath, cancellationToken);
        if (!string.Equals(computedHash, manifestFile.Hash, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError(
                "Integrity verification failed for installer '{Target}'. Expected: {Expected}, Computed: {Computed}",
                step.TargetRelativePath,
                manifestFile.Hash,
                computedHash);

            return OperationResult.CreateFailure(
                $"Integrity verification failed for installer '{step.TargetRelativePath}'.");
        }

        logger.LogDebug("Integrity verified for installer '{Target}'", step.TargetRelativePath);

        // 4. User notification
        var displayTitle = !string.IsNullOrWhiteSpace(step.Name) ? step.Name : "Running Installation Step";
        var displayMessage = !string.IsNullOrWhiteSpace(step.StatusMessage)
            ? step.StatusMessage
            : $"Executing verified installer '{step.TargetRelativePath}'";

        notificationService.ShowInfo(
            displayTitle,
            displayMessage,
            NotificationConstants.DefaultAutoDismissMs);

        progress?.Report(new ContentAcquisitionProgress
        {
            Phase = ContentAcquisitionPhase.Extracting,
            CurrentOperation = displayMessage,
            CurrentFile = step.TargetRelativePath ?? string.Empty,
        });

        // 5. Process execution
        logger.LogInformation(
            "Executing verified installer '{Target}' (Elevation: {RequiresElevation}) for manifest {ManifestId}",
            step.TargetRelativePath,
            step.RequiresElevation,
            manifest.Id);

        var startInfo = new ProcessStartInfo
        {
            FileName = targetFullPath,
            WorkingDirectory = workingDirectory,
        };

        if (step.Arguments is { Count: > 0 })
        {
            foreach (var arg in step.Arguments)
            {
                startInfo.ArgumentList.Add(arg);
            }
        }

        if (step.RequiresElevation && OperatingSystem.IsWindows())
        {
            startInfo.UseShellExecute = true;
            startInfo.Verb = "runas";
        }
        else
        {
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                logger.LogError("Failed to start process for installer '{Target}'", step.TargetRelativePath);
                notificationService.ShowError("Installation Step Failed", $"Failed to start installer '{step.Name}'.");
                return OperationResult.CreateFailure($"Failed to start installer '{step.TargetRelativePath}'.");
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(InstallerStepTimeout);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError("Installer step '{StepName}' timed out", step.Name);
                notificationService.ShowError("Installation Step Failed", $"Step '{step.Name}' timed out.");
                return OperationResult.CreateFailure($"Installation step '{step.Name}' timed out.");
            }

            if (process.ExitCode != 0)
            {
                logger.LogError(
                    "Installer step '{StepName}' exited with error code {ExitCode}",
                    step.Name,
                    process.ExitCode);

                notificationService.ShowError(
                    "Installation Step Failed",
                    $"Step '{step.Name}' failed with exit code {process.ExitCode}.");

                return OperationResult.CreateFailure(
                    $"Installation step '{step.Name}' failed with exit code {process.ExitCode}.");
            }

            logger.LogInformation("Successfully completed installer step '{StepName}'", step.Name);
            notificationService.ShowSuccess(
                "Installation Step Completed",
                $"Successfully completed '{step.Name}'.");

            return OperationResult.CreateSuccess();
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Installation step '{StepName}' was canceled", step.Name);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to execute installer step '{StepName}'", step.Name);
            notificationService.ShowError(
                "Installation Step Error",
                $"Error executing '{step.Name}': {ex.Message}");

            return OperationResult.CreateFailure($"Execution of step '{step.Name}' failed: {ex.Message}");
        }
    }

    private OperationResult ExecuteRemoveFile(InstallationStep step, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(step.TargetRelativePath))
        {
            return OperationResult.CreateFailure($"Target relative path is required for remove file step '{step.Name}'.");
        }

        var normalizedRelativePath = PathHelper.NormalizeRelativePath(step.TargetRelativePath);
        var targetFullPath = Path.Combine(workingDirectory, normalizedRelativePath);

        if (!PathHelper.IsPathContainedIn(targetFullPath, workingDirectory))
        {
            logger.LogError("Target remove path '{Target}' escapes working directory '{Dir}'", step.TargetRelativePath, workingDirectory);
            return OperationResult.CreateFailure($"Target file '{step.TargetRelativePath}' escapes the working directory.");
        }

        try
        {
            if (File.Exists(targetFullPath))
            {
                File.Delete(targetFullPath);
                logger.LogInformation("Deleted file '{Target}' as part of step '{StepName}'", step.TargetRelativePath, step.Name);
            }
            else
            {
                logger.LogDebug("File '{Target}' already absent during remove step '{StepName}'", step.TargetRelativePath, step.Name);
            }

            return OperationResult.CreateSuccess();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete file '{Target}' in step '{StepName}'", step.TargetRelativePath, step.Name);
            return OperationResult.CreateFailure($"Failed to delete file '{step.TargetRelativePath}': {ex.Message}");
        }
    }

    private OperationResult ExecuteRenameFile(InstallationStep step, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(step.TargetRelativePath))
        {
            return OperationResult.CreateFailure($"Target relative path is required for rename step '{step.Name}'.");
        }

        if (string.IsNullOrWhiteSpace(step.DestinationRelativePath))
        {
            return OperationResult.CreateFailure($"Destination relative path is required for rename step '{step.Name}'.");
        }

        var normalizedSourcePath = PathHelper.NormalizeRelativePath(step.TargetRelativePath);
        var normalizedDestPath = PathHelper.NormalizeRelativePath(step.DestinationRelativePath);

        var sourceFullPath = Path.Combine(workingDirectory, normalizedSourcePath);
        var destFullPath = Path.Combine(workingDirectory, normalizedDestPath);

        if (!PathHelper.IsPathContainedIn(sourceFullPath, workingDirectory))
        {
            logger.LogError("Source path '{Source}' escapes working directory '{Dir}'", step.TargetRelativePath, workingDirectory);
            return OperationResult.CreateFailure($"Source path '{step.TargetRelativePath}' escapes the working directory.");
        }

        if (!PathHelper.IsPathContainedIn(destFullPath, workingDirectory))
        {
            logger.LogError("Destination path '{Dest}' escapes working directory '{Dir}'", step.DestinationRelativePath, workingDirectory);
            return OperationResult.CreateFailure($"Destination path '{step.DestinationRelativePath}' escapes the working directory.");
        }

        try
        {
            if (File.Exists(sourceFullPath))
            {
                var destDir = Path.GetDirectoryName(destFullPath);
                if (!string.IsNullOrEmpty(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                File.Move(sourceFullPath, destFullPath, overwrite: true);
                logger.LogInformation(
                    "Renamed '{Source}' to '{Dest}' in step '{StepName}'",
                    step.TargetRelativePath,
                    step.DestinationRelativePath,
                    step.Name);
            }
            else
            {
                logger.LogWarning("Source file '{Source}' does not exist for rename step '{StepName}'", step.TargetRelativePath, step.Name);
            }

            return OperationResult.CreateSuccess();
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to rename '{Source}' to '{Dest}' in step '{StepName}'",
                step.TargetRelativePath,
                step.DestinationRelativePath,
                step.Name);

            return OperationResult.CreateFailure(
                $"Failed to rename '{step.TargetRelativePath}' to '{step.DestinationRelativePath}': {ex.Message}");
        }
    }
}
