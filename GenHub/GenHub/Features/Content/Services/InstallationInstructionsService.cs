using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
/// <param name="localizationService">The optional localization service for user-facing notifications.</param>
public class InstallationInstructionsService(
    IFileHashProvider hashProvider,
    INotificationService notificationService,
    IUserSettingsService? userSettingsService,
    IEnumerable<IInstallationStepPrecondition>? preconditions,
    ILogger<InstallationInstructionsService> logger,
    ILocalizationService? localizationService = null) : IInstallationInstructionsService
{
    private static readonly TimeSpan InstallerStepTimeout = TimeSpan.FromMinutes(10);
    private readonly SemaphoreSlim _executionGate = new(1, 1);

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
    public async Task<OperationResult> ExecutePostInstallStepsAsync(
        ContentManifest manifest,
        string workingDirectory,
        string? providerSource = null,
        bool force = false,
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
            "Executing {Count} post-install step(s) for manifest {ManifestId} from provider {Provider} (force: {Force})",
            manifest.InstallationInstructions.PostInstallSteps.Count,
            manifest.Id,
            providerSource ?? "unspecified",
            force);

        return await ExecuteStepsAsync(
            manifest.InstallationInstructions.PostInstallSteps,
            manifest,
            workingDirectory,
            providerSource,
            force,
            progress,
            cancellationToken);
    }

    /// <summary>
    /// Copies a step with resolved arguments. The step key is dropped so it derives
    /// from the resolved values: a publisher-side value change then naturally
    /// re-triggers RunOnce steps instead of reusing a stale key.
    /// </summary>
    /// <param name="step">The declared step.</param>
    /// <param name="resolvedArguments">The binding-resolved arguments.</param>
    /// <returns>The executable step.</returns>
    private static InstallationStep CopyStepWithArguments(InstallationStep step, List<string> resolvedArguments)
    {
        return new InstallationStep
        {
            Name = step.Name,
            Kind = step.Kind,
            TargetRelativePath = step.TargetRelativePath,
            DestinationRelativePath = step.DestinationRelativePath,
            Arguments = resolvedArguments,
            ArgumentBindings = step.ArgumentBindings,
            RequiresElevation = step.RequiresElevation,
            StatusMessage = step.StatusMessage,
            StepKey = null,
            RunOnce = step.RunOnce,
        };
    }

    private async Task<OperationResult> ExecuteStepsAsync(
        IReadOnlyList<InstallationStep> steps,
        ContentManifest manifest,
        string workingDirectory,
        string? providerSource,
        bool force,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            return OperationResult.CreateFailure($"Working directory does not exist: '{workingDirectory}'");
        }

        await _executionGate.WaitAsync(cancellationToken);
        try
        {
            for (var i = 0; i < steps.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var step = steps[i];

                if (step == null)
                {
                    continue;
                }

                var stepResult = await ExecuteSingleStepAsync(step, manifest, workingDirectory, providerSource, force, progress, cancellationToken);
                if (!stepResult.Success)
                {
                    return stepResult;
                }
            }

            return OperationResult.CreateSuccess();
        }
        finally
        {
            _executionGate.Release();
        }
    }

    private async Task<OperationResult> ExecuteSingleStepAsync(
        InstallationStep step,
        ContentManifest manifest,
        string workingDirectory,
        string? providerSource,
        bool force,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var effectiveStep = step;
        if (step.ArgumentBindings is { Count: > 0 })
        {
            var argumentsResult = InstallationArgumentBindingResolver.ResolveArguments(step, workingDirectory);
            if (!argumentsResult.Success || argumentsResult.Data is null)
            {
                logger.LogError(
                    "Failed to resolve argument bindings for installation step '{StepName}' of manifest {ManifestId}: {Error}",
                    step.Name,
                    manifest.Id,
                    argumentsResult.FirstError);
                return OperationResult.CreateFailure(argumentsResult.Errors);
            }

            effectiveStep = CopyStepWithArguments(step, argumentsResult.Data);
            logger.LogDebug(
                "Resolved {Count} argument binding(s) for installation step '{StepName}' of manifest {ManifestId}",
                step.ArgumentBindings.Count,
                step.Name,
                manifest.Id);
        }

        var stepKey = GetStepKey(effectiveStep, manifest);

        if (!force && step.RunOnce && await ShouldSkipStepAsync(effectiveStep, stepKey, manifest, cancellationToken))
        {
            logger.LogInformation(
                "Skipping installation step '{StepName}' for manifest {ManifestId} because it has already been executed (key: {StepKey})",
                step.Name,
                manifest.Id,
                stepKey);

            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Delivering,
                CurrentOperation = $"Skipping {step.Name} (already installed)",
                CurrentFile = step.TargetRelativePath ?? string.Empty,
            });

            return OperationResult.CreateSuccess();
        }

        var authResult = ValidateProviderAuthorization(providerSource, manifest, effectiveStep);
        if (!authResult.Success)
        {
            return authResult;
        }

        var result = OperationResult.CreateFailure("Uninitialized step result");
        switch (effectiveStep.Kind)
        {
            case InstallationStepKind.RunVerifiedInstaller:
                result = await ExecuteRunVerifiedInstallerAsync(effectiveStep, manifest, workingDirectory, progress, cancellationToken);
                break;

            case InstallationStepKind.RemoveFile:
                result = ExecuteRemoveFile(effectiveStep, workingDirectory);
                break;

            case InstallationStepKind.RenameFile:
                result = ExecuteRenameFile(effectiveStep, workingDirectory);
                break;

            default:
                logger.LogError("Unsupported installation step kind '{Kind}' in step '{StepName}'", effectiveStep.Kind, effectiveStep.Name);
                return OperationResult.CreateFailure($"Unsupported installation step kind '{effectiveStep.Kind}' for step '{effectiveStep.Name}'.");
        }

        if (result.Success && step.RunOnce && !string.IsNullOrWhiteSpace(stepKey))
        {
            await RecordStepExecutedAsync(stepKey, cancellationToken);
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
                    if (!string.IsNullOrWhiteSpace(stepKey))
                    {
                        await RecordStepExecutedAsync(stepKey, cancellationToken);
                    }

                    return true;
                }
            }
        }

        return false;
    }

    private async Task RecordStepExecutedAsync(string stepKey, CancellationToken cancellationToken)
    {
        if (userSettingsService == null || string.IsNullOrWhiteSpace(stepKey))
        {
            return;
        }

        userSettingsService.Update(s => s.RecordInstallationStepExecuted(stepKey));

        try
        {
            await userSettingsService.SaveAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist executed installation step key '{StepKey}'", stepKey);
        }
    }

    private string GetStepKey(InstallationStep step, ContentManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(step.StepKey))
        {
            return step.StepKey;
        }

        var publisher = manifest.Publisher?.PublisherType ?? "generic";
        var manifestId = manifest.Id.Value ?? string.Empty;
        var name = step.Name;
        var target = step.TargetRelativePath ?? string.Empty;
        var args = step.Arguments is { Count: > 0 }
            ? string.Join(" ", step.Arguments.Select(QuoteArgumentIfNeeded))
            : string.Empty;

        return $"{publisher}:{manifestId}:{name}:{target}:{args}".TrimEnd(':');

        static string QuoteArgumentIfNeeded(string arg)
        {
            if (string.IsNullOrEmpty(arg))
            {
                return "\"\"";
            }

            if (arg.IndexOfAny([' ', '\t', '"', '\'']) >= 0)
            {
                return $"\"{arg.Replace("\"", "\\\"")}\"";
            }

            return arg;
        }
    }

    private async Task<OperationResult> ExecuteRunVerifiedInstallerAsync(
        InstallationStep step,
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var pathResult = ValidateInstallerTargetPath(step, workingDirectory, out var targetFullPath);
        if (!pathResult.Success)
        {
            return pathResult;
        }

        var integrityResult = await VerifyInstallerIntegrityAsync(step, manifest, targetFullPath, cancellationToken);
        if (!integrityResult.Success)
        {
            return integrityResult;
        }

        NotifyStepStarting(step, progress);

        logger.LogInformation(
            "Executing verified installer '{Target}' (Elevation: {RequiresElevation}) for manifest {ManifestId}",
            step.TargetRelativePath,
            step.RequiresElevation,
            manifest.Id);

        return await RunInstallerProcessAsync(step, targetFullPath, workingDirectory, cancellationToken);
    }

    private OperationResult ValidateProviderAuthorization(string? providerSource, ContentManifest manifest, InstallationStep step)
    {
        var effectiveSource = !string.IsNullOrWhiteSpace(providerSource)
            ? providerSource
            : string.Empty;

        var isTrusted = PublisherTypeConstants.TrustedExecutablePublishers.Contains(effectiveSource);

        if (!isTrusted)
        {
            logger.LogError(
                "Untrusted provider '{ProviderSource}' attempted to execute step '{StepName}' (Kind: {Kind}) for manifest {ManifestId}",
                effectiveSource,
                step.Name,
                step.Kind,
                manifest.Id);

            return OperationResult.CreateFailure(
                $"Provider '{(!string.IsNullOrEmpty(effectiveSource) ? effectiveSource : "unknown")}' is not authorized to execute installation steps.");
        }

        return OperationResult.CreateSuccess();
    }

    private OperationResult ValidateInstallerTargetPath(InstallationStep step, string workingDirectory, out string targetFullPath)
    {
        targetFullPath = string.Empty;

        if (string.IsNullOrWhiteSpace(step.TargetRelativePath))
        {
            return OperationResult.CreateFailure($"Target relative path is required for executable step '{step.Name}'.");
        }

        var normalizedRelativePath = PathHelper.NormalizeRelativePath(step.TargetRelativePath);
        targetFullPath = Path.Combine(workingDirectory, normalizedRelativePath);

        if (!PathHelper.IsPathWithinDirectory(workingDirectory, targetFullPath))
        {
            logger.LogError("Target installer path '{Target}' escapes working directory '{Dir}'", step.TargetRelativePath, workingDirectory);
            return OperationResult.CreateFailure($"Installer path '{step.TargetRelativePath}' escapes the working directory.");
        }

        if (!File.Exists(targetFullPath))
        {
            logger.LogError("Installer executable not found at '{Path}'", targetFullPath);
            return OperationResult.CreateFailure($"Installer executable '{step.TargetRelativePath}' was not found in delivered content.");
        }

        return OperationResult.CreateSuccess();
    }

    private async Task<OperationResult> VerifyInstallerIntegrityAsync(
        InstallationStep step,
        ContentManifest manifest,
        string targetFullPath,
        CancellationToken cancellationToken)
    {
        var normalizedRelativePath = PathHelper.NormalizeRelativePath(step.TargetRelativePath ?? string.Empty);
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

        var computedHash = string.Empty;
        try
        {
            computedHash = await hashProvider.ComputeFileHashAsync(targetFullPath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to compute hash for installer '{Target}' in manifest {ManifestId}", step.TargetRelativePath, manifest.Id);
            return OperationResult.CreateFailure($"Failed to compute hash for installer '{step.TargetRelativePath}': {ex.Message}");
        }

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
        return OperationResult.CreateSuccess();
    }

    private void NotifyStepStarting(InstallationStep step, IProgress<ContentAcquisitionProgress>? progress)
    {
        var displayTitle = !string.IsNullOrWhiteSpace(step.Name) ? step.Name : localizationService.GetLocalizedString("Content.Notification.InstallationStepRunning.Title", "Running Installation Step");
        var displayMessage = !string.IsNullOrWhiteSpace(step.StatusMessage)
            ? step.StatusMessage
            : $"Executing verified installer '{step.TargetRelativePath}'";

        notificationService.ShowInfo(
            displayTitle,
            displayMessage,
            NotificationConstants.DefaultAutoDismissMs);

        progress?.Report(new ContentAcquisitionProgress
        {
            Phase = ContentAcquisitionPhase.Delivering,
            CurrentOperation = displayMessage,
            CurrentFile = step.TargetRelativePath ?? string.Empty,
        });
    }

    private async Task<OperationResult> RunInstallerProcessAsync(
        InstallationStep step,
        string targetFullPath,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
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

        if (step.RequiresElevation)
        {
            if (!OperatingSystem.IsWindows())
            {
                logger.LogError("Installation step '{StepName}' requires administrator elevation, which is only supported on Windows", step.Name);
                return OperationResult.CreateFailure(
                    $"Installation step '{step.Name}' requires administrator elevation, which is only supported on Windows.");
            }

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
                notificationService.ShowError(
                    localizationService.GetLocalizedString("Content.Notification.InstallationStepFailed.Title", "Installation Step Failed"),
                    localizationService.GetLocalizedString("Content.Notification.InstallationStepFailed.StartMessage", $"Failed to start installer '{step.Name}'.", step.Name));
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
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(CancellationToken.None);
                    }
                }
                catch (Exception killEx)
                {
                    logger.LogWarning(killEx, "Failed to terminate timed-out installer step '{StepName}'", step.Name);
                }

                notificationService.ShowError(
                    localizationService.GetLocalizedString("Content.Notification.InstallationStepFailed.Title", "Installation Step Failed"),
                    localizationService.GetLocalizedString("Content.Notification.InstallationStepFailed.TimeoutMessage", $"Step '{step.Name}' timed out.", step.Name));
                return OperationResult.CreateFailure($"Installation step '{step.Name}' timed out.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation("Installation step '{StepName}' was canceled by caller, killing process tree", step.Name);
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(CancellationToken.None);
                    }
                }
                catch (Exception killEx)
                {
                    logger.LogWarning(killEx, "Failed to terminate canceled installer step '{StepName}'", step.Name);
                }

                throw;
            }

            if (process.ExitCode != 0)
            {
                logger.LogError(
                    "Installer step '{StepName}' exited with error code {ExitCode}",
                    step.Name,
                    process.ExitCode);

                notificationService.ShowError(
                    localizationService.GetLocalizedString("Content.Notification.InstallationStepFailed.Title", "Installation Step Failed"),
                    localizationService.GetLocalizedString("Content.Notification.InstallationStepFailed.ExitCodeMessage", $"Step '{step.Name}' failed with exit code {process.ExitCode}.", step.Name, process.ExitCode));

                return OperationResult.CreateFailure(
                    $"Installation step '{step.Name}' failed with exit code {process.ExitCode}.");
            }

            logger.LogInformation("Successfully completed installer step '{StepName}'", step.Name);
            notificationService.ShowSuccess(
                localizationService.GetLocalizedString("Content.Notification.InstallationStepCompleted.Title", "Installation Step Completed"),
                localizationService.GetLocalizedString("Content.Notification.InstallationStepCompleted.Message", $"Successfully completed '{step.Name}'.", step.Name));

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
                localizationService.GetLocalizedString("Content.Notification.InstallationStepError.Title", "Installation Step Error"),
                localizationService.GetLocalizedString("Content.Notification.InstallationStepError.Message", $"Error executing '{step.Name}': {ex.Message}", step.Name, ex.Message));

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

        if (!PathHelper.IsPathWithinDirectory(workingDirectory, targetFullPath))
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

        if (!PathHelper.IsPathWithinDirectory(workingDirectory, sourceFullPath))
        {
            logger.LogError("Source path '{Source}' escapes working directory '{Dir}'", step.TargetRelativePath, workingDirectory);
            return OperationResult.CreateFailure($"Source path '{step.TargetRelativePath}' escapes the working directory.");
        }

        if (!PathHelper.IsPathWithinDirectory(workingDirectory, destFullPath))
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
