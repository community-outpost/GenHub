using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                Variant = context.Variant,
            };

            foreach (var (manifestId, version) in context.ManifestVersions)
            {
                receipt.ManifestVersions[manifestId] = version;
            }

            foreach (var (variableName, value) in context.EnvironmentVariables)
            {
                receipt.EnvironmentVariables[variableName] = value;
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

        report.Receipt = receipt;
        CompareExecutable(receipt.Executable, report);
        foreach (var (variableName, recordedRoot) in receipt.ArchiveRoots)
        {
            CompareArchiveRoot(variableName, recordedRoot, report);
        }

        return OperationResult<LaunchReceiptDriftReport>.CreateSuccess(report);
    }

    /// <inheritdoc/>
    public LaunchReceiptDriftReport CompareUpcomingLaunch(LaunchReceipt receipt, LaunchReceiptContext upcoming)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(upcoming);

        var report = new LaunchReceiptDriftReport { HasReceipt = true, Receipt = receipt };

        if (!string.Equals(receipt.GameClientId ?? string.Empty, upcoming.GameClientId ?? string.Empty, StringComparison.Ordinal))
        {
            report.DriftedFields.Add(
                $"Game client changed from {receipt.GameClientId ?? "(none)"} to {upcoming.GameClientId ?? "(none)"}");
        }

        if (receipt.GameType != upcoming.GameType)
        {
            report.DriftedFields.Add($"Game type changed from {receipt.GameType} to {upcoming.GameType}");
        }

        if (!string.IsNullOrEmpty(upcoming.ExecutablePath) &&
            !PathsEqual(receipt.Executable.Path, upcoming.ExecutablePath))
        {
            report.DriftedFields.Add(
                $"Executable path changed from {receipt.Executable.Path} to {upcoming.ExecutablePath}");
        }

        CompareManifests(receipt, upcoming, report);
        CompareArchiveRootConfiguration(receipt, upcoming, report);
        CompareEnvironment(receipt, upcoming, report);
        CompareVariant(receipt.Variant, upcoming.Variant, report);

        return report;
    }

    /// <summary>
    /// Resolves the receipt path within a workspace.
    /// </summary>
    /// <param name="workspacePath">The workspace directory.</param>
    /// <returns>The receipt path.</returns>
    private static string GetReceiptPath(string workspacePath) =>
        Path.Combine(workspacePath, FileTypes.LaunchReceiptFileName);

    /// <summary>
    /// Compares two paths ignoring a trailing directory separator, which the archive root
    /// variables carry by engine requirement and other paths do not.
    /// </summary>
    /// <param name="left">One path.</param>
    /// <param name="right">The other path.</param>
    /// <returns>Whether the paths are equal.</returns>
    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            StringComparison.Ordinal);

    /// <summary>
    /// Compares the recorded manifest set and versions against the upcoming launch's.
    /// </summary>
    /// <param name="receipt">The receipt from the previous launch.</param>
    /// <param name="upcoming">The configuration of the launch about to happen.</param>
    /// <param name="report">The report drifted fields are added to.</param>
    private static void CompareManifests(LaunchReceipt receipt, LaunchReceiptContext upcoming, LaunchReceiptDriftReport report)
    {
        var recordedIds = new HashSet<string>(receipt.ManifestIds, StringComparer.Ordinal);
        var upcomingIds = new HashSet<string>(upcoming.ManifestIds, StringComparer.Ordinal);

        foreach (var manifestId in receipt.ManifestIds)
        {
            if (!upcomingIds.Contains(manifestId))
            {
                report.DriftedFields.Add($"Manifest no longer part of the launch: {manifestId}");
            }
        }

        foreach (var manifestId in upcoming.ManifestIds)
        {
            if (!recordedIds.Contains(manifestId))
            {
                report.DriftedFields.Add($"Manifest added since the last launch: {manifestId}");
            }
        }

        foreach (var (manifestId, recordedVersion) in receipt.ManifestVersions)
        {
            if (upcoming.ManifestVersions.TryGetValue(manifestId, out var upcomingVersion) &&
                !string.Equals(recordedVersion, upcomingVersion, StringComparison.Ordinal))
            {
                report.DriftedFields.Add(
                    $"Manifest {manifestId} version changed from {recordedVersion} to {upcomingVersion}");
            }
        }
    }

    /// <summary>
    /// Compares which archive roots are configured, and where they point, against the
    /// receipt — path changes, not content changes, which the filesystem checks own.
    /// </summary>
    /// <param name="receipt">The receipt from the previous launch.</param>
    /// <param name="upcoming">The configuration of the launch about to happen.</param>
    /// <param name="report">The report drifted fields are added to.</param>
    private static void CompareArchiveRootConfiguration(LaunchReceipt receipt, LaunchReceiptContext upcoming, LaunchReceiptDriftReport report)
    {
        foreach (var variableName in RetailArchiveConstants.InstallPathVariables)
        {
            receipt.ArchiveRoots.TryGetValue(variableName, out var recordedRoot);
            var upcomingRoot =
                upcoming.EnvironmentVariables.TryGetValue(variableName, out var configured) &&
                !string.IsNullOrWhiteSpace(configured)
                    ? configured
                    : null;

            if (recordedRoot is null && upcomingRoot is null)
            {
                continue;
            }

            if (recordedRoot is null)
            {
                report.DriftedFields.Add($"Archive root for {variableName} newly configured: {upcomingRoot}");
                continue;
            }

            if (upcomingRoot is null)
            {
                report.DriftedFields.Add($"Archive root for {variableName} no longer configured; was {recordedRoot.Path}");
                continue;
            }

            if (!PathsEqual(recordedRoot.Path, upcomingRoot))
            {
                report.DriftedFields.Add(
                    $"Archive root path for {variableName} changed from {recordedRoot.Path} to {upcomingRoot}");
            }
        }
    }

    /// <summary>
    /// Compares the GenHub-built child environment against the receipt, per variable. The
    /// retail archive root variables are excluded here because
    /// <see cref="CompareArchiveRootConfiguration"/> already names their changes.
    /// </summary>
    /// <param name="receipt">The receipt from the previous launch.</param>
    /// <param name="upcoming">The configuration of the launch about to happen.</param>
    /// <param name="report">The report drifted fields are added to.</param>
    private static void CompareEnvironment(LaunchReceipt receipt, LaunchReceiptContext upcoming, LaunchReceiptDriftReport report)
    {
        foreach (var (variableName, recordedValue) in receipt.EnvironmentVariables)
        {
            if (IsArchiveRootVariable(variableName))
            {
                continue;
            }

            if (!upcoming.EnvironmentVariables.TryGetValue(variableName, out var upcomingValue))
            {
                report.DriftedFields.Add($"Environment variable {variableName} no longer set; was {recordedValue}");
            }
            else if (!string.Equals(recordedValue, upcomingValue, StringComparison.Ordinal))
            {
                report.DriftedFields.Add(
                    $"Environment variable {variableName} changed from {recordedValue} to {upcomingValue}");
            }
        }

        foreach (var (variableName, upcomingValue) in upcoming.EnvironmentVariables)
        {
            if (!IsArchiveRootVariable(variableName) &&
                !receipt.EnvironmentVariables.ContainsKey(variableName))
            {
                report.DriftedFields.Add($"Environment variable {variableName} newly set: {upcomingValue}");
            }
        }
    }

    /// <summary>
    /// Determines whether a variable is one of the retail archive root variables.
    /// </summary>
    /// <param name="variableName">The variable name.</param>
    /// <returns>Whether it carries an archive root.</returns>
    private static bool IsArchiveRootVariable(string variableName)
    {
        foreach (var rootVariable in RetailArchiveConstants.InstallPathVariables)
        {
            if (string.Equals(variableName, rootVariable, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Compares the resolved variant and entry-point identity against the receipt.
    /// </summary>
    /// <param name="recorded">The identity recorded at the previous launch, if any.</param>
    /// <param name="upcoming">The identity resolved for the launch about to happen, if any.</param>
    /// <param name="report">The report drifted fields are added to.</param>
    private static void CompareVariant(LaunchReceiptVariant? recorded, LaunchReceiptVariant? upcoming, LaunchReceiptDriftReport report)
    {
        if (recorded is null && upcoming is null)
        {
            return;
        }

        if (recorded is null)
        {
            report.DriftedFields.Add(
                $"Variant identity newly resolvable; entry point is {upcoming!.EntryPointRelativePath ?? "(unresolved)"}");
            return;
        }

        if (upcoming is null)
        {
            report.DriftedFields.Add(
                $"Variant identity no longer resolvable; entry point was {recorded.EntryPointRelativePath ?? "(unresolved)"}");
            return;
        }

        if (!string.Equals(recorded.RuntimeIdentifier, upcoming.RuntimeIdentifier, StringComparison.OrdinalIgnoreCase))
        {
            report.DriftedFields.Add(
                $"Host runtime identifier changed from {recorded.RuntimeIdentifier} to {upcoming.RuntimeIdentifier}");
        }

        if (!recorded.VariantRuntimeIdentifiers.SequenceEqual(upcoming.VariantRuntimeIdentifiers, StringComparer.OrdinalIgnoreCase))
        {
            report.DriftedFields.Add(
                $"Resolved variant changed from [{string.Join(", ", recorded.VariantRuntimeIdentifiers)}] to [{string.Join(", ", upcoming.VariantRuntimeIdentifiers)}]");
        }

        if (!string.Equals(recorded.EntryPointRelativePath, upcoming.EntryPointRelativePath, StringComparison.Ordinal))
        {
            report.DriftedFields.Add(
                $"Entry point changed from {recorded.EntryPointRelativePath ?? "(unresolved)"} to {upcoming.EntryPointRelativePath ?? "(unresolved)"}");
        }
    }

    /// <summary>
    /// Fingerprints one archive root as a per-archive list of name, size and timestamp
    /// from a single directory listing, with the count and byte total derived from it.
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
            var info = new FileInfo(archivePath);
            fingerprint.Archives.Add(new LaunchReceiptArchiveEntry
            {
                FileName = info.Name,
                SizeBytes = info.Length,
                LastWriteUtc = info.LastWriteTimeUtc,
            });
            fingerprint.ArchiveCount++;
            fingerprint.TotalArchiveBytes += info.Length;
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
    /// Compares one recorded archive-root fingerprint against the directory on disk,
    /// naming each added, removed or changed archive. Size and timestamp per archive make
    /// an equal-size replacement visible, which a count and byte total alone cannot see.
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

        var recordedByName = IndexArchivesByName(recorded.Archives);
        var currentByName = IndexArchivesByName(current.Archives);

        foreach (var (fileName, recordedArchive) in recordedByName)
        {
            if (!currentByName.TryGetValue(fileName, out var currentArchive))
            {
                report.DriftedFields.Add($"Archive removed from root for {variableName}: {recordedArchive.FileName}");
                continue;
            }

            if (currentArchive.SizeBytes != recordedArchive.SizeBytes)
            {
                report.DriftedFields.Add(
                    $"Archive {recordedArchive.FileName} in root for {variableName} changed size from {recordedArchive.SizeBytes} to {currentArchive.SizeBytes} bytes");
            }
            else if (currentArchive.LastWriteUtc != recordedArchive.LastWriteUtc)
            {
                report.DriftedFields.Add(
                    $"Archive {recordedArchive.FileName} in root for {variableName} changed last-write time from {recordedArchive.LastWriteUtc:O} to {currentArchive.LastWriteUtc:O}");
            }
        }

        foreach (var (fileName, currentArchive) in currentByName)
        {
            if (!recordedByName.ContainsKey(fileName))
            {
                report.DriftedFields.Add($"Archive added to root for {variableName}: {currentArchive.FileName}");
            }
        }
    }

    /// <summary>
    /// Indexes archive fingerprints by file name, case-insensitively to match how the
    /// archives themselves are enumerated.
    /// </summary>
    /// <param name="archives">The fingerprints to index.</param>
    /// <returns>The index.</returns>
    private static Dictionary<string, LaunchReceiptArchiveEntry> IndexArchivesByName(IEnumerable<LaunchReceiptArchiveEntry> archives)
    {
        var index = new Dictionary<string, LaunchReceiptArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var archive in archives)
        {
            index[archive.FileName] = archive;
        }

        return index;
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
