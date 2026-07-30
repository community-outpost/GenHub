using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Launching;

/// <summary>
/// Records launch receipts into the workspace and cheaply revalidates them before
/// subsequent launches.
/// </summary>
/// <remarks>
/// The receipt is a single JSON file beside the workspace content; the latest launch wins.
/// Recording hashes the executable once, but revalidation recomputes only cheap fields —
/// existence, counts, sizes and timestamps — so it can run on every launch. Archive roots
/// are fingerprinted by archive count and total bytes rather than content, because hashing
/// gigabytes of retail archives per launch would defeat the point.
/// </remarks>
public class LaunchReceiptService(
    ILogger<LaunchReceiptService> logger,
    IFileHashProvider hashProvider) : ILaunchReceiptService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <inheritdoc/>
    public async Task<OperationResult<LaunchReceipt>> RecordLaunchAsync(LaunchReceiptContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            var receipt = new LaunchReceipt
            {
                RecordedAtUtc = DateTime.UtcNow,
                LaunchId = context.LaunchId,
                ProfileId = context.ProfileId,
                GameClientId = context.GameClientId,
                GameType = context.GameType,
                WorkspaceId = context.WorkspaceId,
                WorkingDirectory = context.WorkingDirectory,
                Executable = await FingerprintExecutableAsync(context.ExecutablePath, cancellationToken),
                ManifestIds = [.. context.ManifestIds],
            };

            foreach (var (manifestId, version) in context.ManifestVersions)
            {
                receipt.ManifestVersions[manifestId] = version;
            }

            foreach (var variableName in RetailArchiveConstants.InstallPathVariables)
            {
                if (context.EnvironmentVariables.TryGetValue(variableName, out var root) &&
                    !string.IsNullOrWhiteSpace(root))
                {
                    receipt.ArchiveRoots[variableName] = FingerprintArchiveRoot(root);
                }
            }

            var receiptPath = GetReceiptPath(context.WorkspacePath);
            var temporaryPath = receiptPath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(receipt, JsonOptions), cancellationToken);
            File.Move(temporaryPath, receiptPath, overwrite: true);

            logger.LogDebug("Launch receipt recorded at {ReceiptPath}", receiptPath);
            return OperationResult<LaunchReceipt>.CreateSuccess(receipt);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to record launch receipt for profile {ProfileId}", context.ProfileId);
            return OperationResult<LaunchReceipt>.CreateFailure($"Failed to record launch receipt: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<LaunchReceiptDriftReport>> RevalidateAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

        var receiptPath = GetReceiptPath(workspacePath);
        var report = new LaunchReceiptDriftReport { ReceiptPath = receiptPath };

        if (!File.Exists(receiptPath))
        {
            return OperationResult<LaunchReceiptDriftReport>.CreateSuccess(report);
        }

        report.HasReceipt = true;

        LaunchReceipt? receipt;
        try
        {
            var json = await File.ReadAllTextAsync(receiptPath, cancellationToken);
            receipt = JsonSerializer.Deserialize<LaunchReceipt>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            report.DriftedFields.Add($"Receipt could not be read: {receiptPath} ({ex.Message})");
            return OperationResult<LaunchReceiptDriftReport>.CreateSuccess(report);
        }

        if (receipt is null)
        {
            report.DriftedFields.Add($"Receipt is empty: {receiptPath}");
            return OperationResult<LaunchReceiptDriftReport>.CreateSuccess(report);
        }

        CompareExecutable(receipt.Executable, report);
        foreach (var (variableName, recordedRoot) in receipt.ArchiveRoots)
        {
            CompareArchiveRoot(variableName, recordedRoot, report);
        }

        return OperationResult<LaunchReceiptDriftReport>.CreateSuccess(report);
    }

    /// <summary>
    /// Resolves the receipt path within a workspace.
    /// </summary>
    /// <param name="workspacePath">The workspace directory.</param>
    /// <returns>The receipt path.</returns>
    private static string GetReceiptPath(string workspacePath) =>
        Path.Combine(workspacePath, FileTypes.LaunchReceiptFileName);

    /// <summary>
    /// Fingerprints one archive root by archive count and total bytes.
    /// </summary>
    /// <param name="rootPath">The archive root.</param>
    /// <returns>The fingerprint; a missing root fingerprints as zero archives.</returns>
    private static LaunchReceiptArchiveRoot FingerprintArchiveRoot(string rootPath)
    {
        var fingerprint = new LaunchReceiptArchiveRoot { Path = rootPath };
        if (!Directory.Exists(rootPath))
        {
            return fingerprint;
        }

        foreach (var archivePath in Directory.EnumerateFiles(
            rootPath, RetailArchiveConstants.ArchiveSearchPattern, RetailArchiveConstants.ArchiveSearch))
        {
            fingerprint.ArchiveCount++;
            fingerprint.TotalArchiveBytes += new FileInfo(archivePath).Length;
        }

        return fingerprint;
    }

    /// <summary>
    /// Compares the recorded executable fingerprint against the file on disk, by size and
    /// last-write time only — the recorded hash is for after-the-fact comparison, not for
    /// recomputation on every launch.
    /// </summary>
    /// <param name="recorded">The recorded fingerprint.</param>
    /// <param name="report">The report drifted fields are added to.</param>
    private static void CompareExecutable(LaunchReceiptExecutable recorded, LaunchReceiptDriftReport report)
    {
        if (string.IsNullOrEmpty(recorded.Path))
        {
            return;
        }

        var current = new FileInfo(recorded.Path);
        if (!current.Exists)
        {
            report.DriftedFields.Add($"Executable no longer exists: {recorded.Path}");
            return;
        }

        if (current.Length != recorded.SizeBytes)
        {
            report.DriftedFields.Add(
                $"Executable size changed from {recorded.SizeBytes} to {current.Length} bytes: {recorded.Path}");
        }

        if (current.LastWriteTimeUtc != recorded.LastWriteUtc)
        {
            report.DriftedFields.Add(
                $"Executable last-write time changed from {recorded.LastWriteUtc:O} to {current.LastWriteTimeUtc:O}: {recorded.Path}");
        }
    }

    /// <summary>
    /// Compares one recorded archive-root fingerprint against the directory on disk.
    /// </summary>
    /// <param name="variableName">The environment variable that carried the root.</param>
    /// <param name="recorded">The recorded fingerprint.</param>
    /// <param name="report">The report drifted fields are added to.</param>
    private static void CompareArchiveRoot(string variableName, LaunchReceiptArchiveRoot recorded, LaunchReceiptDriftReport report)
    {
        if (!Directory.Exists(recorded.Path))
        {
            report.DriftedFields.Add($"Archive root for {variableName} no longer exists: {recorded.Path}");
            return;
        }

        LaunchReceiptArchiveRoot current;
        try
        {
            current = FingerprintArchiveRoot(recorded.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            report.DriftedFields.Add($"Archive root for {variableName} could not be read: {recorded.Path} ({ex.Message})");
            return;
        }

        if (current.ArchiveCount != recorded.ArchiveCount)
        {
            report.DriftedFields.Add(
                $"Archive count for {variableName} changed from {recorded.ArchiveCount} to {current.ArchiveCount}: {recorded.Path}");
        }

        if (current.TotalArchiveBytes != recorded.TotalArchiveBytes)
        {
            report.DriftedFields.Add(
                $"Total archive bytes for {variableName} changed from {recorded.TotalArchiveBytes} to {current.TotalArchiveBytes}: {recorded.Path}");
        }
    }

    /// <summary>
    /// Fingerprints the executable, including the one hash recording pays for.
    /// </summary>
    /// <param name="executablePath">The executable being launched.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>The fingerprint; a missing executable fingerprints as path only.</returns>
    private async Task<LaunchReceiptExecutable> FingerprintExecutableAsync(string executablePath, CancellationToken cancellationToken)
    {
        var fingerprint = new LaunchReceiptExecutable { Path = executablePath };
        var info = new FileInfo(executablePath);
        if (!info.Exists)
        {
            return fingerprint;
        }

        fingerprint.SizeBytes = info.Length;
        fingerprint.LastWriteUtc = info.LastWriteTimeUtc;
        fingerprint.Sha256 = await hashProvider.ComputeFileHashAsync(executablePath, cancellationToken);
        return fingerprint;
    }
}
