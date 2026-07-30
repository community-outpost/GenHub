using System.Text.Json;
using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Launching;
using GenHub.Features.Launching;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Launching;

/// <summary>
/// Tests for <see cref="LaunchReceiptService"/>.
/// </summary>
public class LaunchReceiptServiceTests : IDisposable
{
    private readonly LaunchReceiptService _service = new(
        new Mock<ILogger<LaunchReceiptService>>().Object,
        new Sha256HashProvider());

    private readonly string _root;
    private readonly string _workspacePath;
    private readonly string _archiveRoot;
    private readonly string _executablePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="LaunchReceiptServiceTests"/> class.
    /// </summary>
    public LaunchReceiptServiceTests()
    {
        _root = Directory.CreateTempSubdirectory("GenHub.LaunchReceiptServiceTests.").FullName;
        _workspacePath = Directory.CreateDirectory(Path.Combine(_root, "workspace")).FullName;
        _archiveRoot = Directory.CreateDirectory(Path.Combine(_root, "retail")).FullName;
        _executablePath = Path.Combine(_workspacePath, "generalszh");

        File.WriteAllText(_executablePath, "executable bytes");
        File.WriteAllText(Path.Combine(_archiveRoot, "INIZH.big"), "archive one");
        File.WriteAllText(Path.Combine(_archiveRoot, "TexturesZH.big"), "archive two!");
    }

    /// <summary>
    /// Recording writes a receipt into the workspace capturing the executable and archive roots.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task RecordLaunchAsync_WritesReceiptIntoWorkspace()
    {
        var result = await _service.RecordLaunchAsync(CreateContext());

        Assert.True(result.Success);
        var receiptPath = Path.Combine(_workspacePath, FileTypes.LaunchReceiptFileName);
        Assert.True(File.Exists(receiptPath));

        var receipt = JsonSerializer.Deserialize<LaunchReceipt>(
            await File.ReadAllTextAsync(receiptPath),
            new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
        Assert.NotNull(receipt);
        Assert.Equal("profile-1", receipt.ProfileId);
        Assert.Equal(GameType.ZeroHour, receipt.GameType);
        Assert.Equal(_executablePath, receipt.Executable.Path);
        Assert.Equal(new FileInfo(_executablePath).Length, receipt.Executable.SizeBytes);
        Assert.NotEmpty(receipt.Executable.Sha256);

        var recordedRoot = Assert.Contains(RetailArchiveConstants.ZeroHourInstallPathVariable, receipt.ArchiveRoots);
        Assert.Equal(2, recordedRoot.ArchiveCount);
        Assert.Equal(
            new FileInfo(Path.Combine(_archiveRoot, "INIZH.big")).Length +
            new FileInfo(Path.Combine(_archiveRoot, "TexturesZH.big")).Length,
            recordedRoot.TotalArchiveBytes);
        Assert.Contains("1.0.genhub.mod.test", receipt.ManifestIds);
    }

    /// <summary>
    /// A subsequent record replaces the previous receipt; the latest launch wins.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task RecordLaunchAsync_ReplacesPreviousReceipt()
    {
        await _service.RecordLaunchAsync(CreateContext(launchId: "first"));
        await _service.RecordLaunchAsync(CreateContext(launchId: "second"));

        var receipt = await ReadReceiptAsync();
        Assert.Equal("second", receipt.LaunchId);
    }

    /// <summary>
    /// Revalidating a workspace without a receipt is not an error and reports nothing.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task RevalidateAsync_WithoutReceipt_ReportsNothing()
    {
        var result = await _service.RevalidateAsync(_workspacePath);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.False(result.Data.HasReceipt);
        Assert.False(result.Data.HasDrift);
    }

    /// <summary>
    /// Revalidating an unchanged state reports the receipt with no drift.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task RevalidateAsync_WithUnchangedState_ReportsNoDrift()
    {
        await _service.RecordLaunchAsync(CreateContext());

        var result = await _service.RevalidateAsync(_workspacePath);

        Assert.True(result.Success);
        Assert.True(result.Data!.HasReceipt);
        Assert.False(result.Data.HasDrift);
    }

    /// <summary>
    /// An archive added to a root since the last launch is reported as count drift.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task RevalidateAsync_WithChangedArchiveCount_ReportsDrift()
    {
        await _service.RecordLaunchAsync(CreateContext());
        File.WriteAllText(Path.Combine(_archiveRoot, "ModZH.big"), "a third archive");

        var result = await _service.RevalidateAsync(_workspacePath);

        Assert.True(result.Success);
        Assert.True(result.Data!.HasDrift);
        Assert.Contains(result.Data.DriftedFields, f =>
            f.Contains("Archive count") && f.Contains(RetailArchiveConstants.ZeroHourInstallPathVariable));
    }

    /// <summary>
    /// A mutated archive that keeps the count but changes total bytes is reported as drift.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task RevalidateAsync_WithMutatedArchiveBytes_ReportsDrift()
    {
        await _service.RecordLaunchAsync(CreateContext());
        File.WriteAllText(Path.Combine(_archiveRoot, "INIZH.big"), "a much longer replacement archive body");

        var result = await _service.RevalidateAsync(_workspacePath);

        Assert.True(result.Success);
        Assert.True(result.Data!.HasDrift);
        Assert.Contains(result.Data.DriftedFields, f =>
            f.Contains("Total archive bytes") && f.Contains(RetailArchiveConstants.ZeroHourInstallPathVariable));
    }

    /// <summary>
    /// A root that disappeared since the last launch is reported as drift.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task RevalidateAsync_WithMissingArchiveRoot_ReportsDrift()
    {
        await _service.RecordLaunchAsync(CreateContext());
        Directory.Delete(_archiveRoot, recursive: true);

        var result = await _service.RevalidateAsync(_workspacePath);

        Assert.True(result.Success);
        Assert.True(result.Data!.HasDrift);
        Assert.Contains(result.Data.DriftedFields, f => f.Contains("no longer exists") && f.Contains(_archiveRoot));
    }

    /// <summary>
    /// A swapped executable is reported by its changed size without rehashing.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task RevalidateAsync_WithSwappedExecutable_ReportsDrift()
    {
        await _service.RecordLaunchAsync(CreateContext());
        File.WriteAllText(_executablePath, "a different executable with a different length");

        var result = await _service.RevalidateAsync(_workspacePath);

        Assert.True(result.Success);
        Assert.True(result.Data!.HasDrift);
        Assert.Contains(result.Data.DriftedFields, f => f.Contains("Executable size changed"));
    }

    /// <summary>
    /// A deleted executable is reported as drift rather than an error.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task RevalidateAsync_WithMissingExecutable_ReportsDrift()
    {
        await _service.RecordLaunchAsync(CreateContext());
        File.Delete(_executablePath);

        var result = await _service.RevalidateAsync(_workspacePath);

        Assert.True(result.Success);
        Assert.True(result.Data!.HasDrift);
        Assert.Contains(result.Data.DriftedFields, f => f.Contains("Executable no longer exists"));
    }

    /// <summary>
    /// An unreadable receipt is reported as drift, not thrown.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task RevalidateAsync_WithCorruptReceipt_ReportsDrift()
    {
        await File.WriteAllTextAsync(Path.Combine(_workspacePath, FileTypes.LaunchReceiptFileName), "{ not json");

        var result = await _service.RevalidateAsync(_workspacePath);

        Assert.True(result.Success);
        Assert.True(result.Data!.HasReceipt);
        Assert.True(result.Data.HasDrift);
        Assert.Contains(result.Data.DriftedFields, f => f.Contains("could not be read"));
    }

    /// <summary>
    /// An upcoming launch identical to the recorded one reports no configuration drift, and
    /// revalidation hands back the parsed receipt for that comparison.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task CompareUpcomingLaunch_WithIdenticalConfiguration_ReportsNoDrift()
    {
        await _service.RecordLaunchAsync(CreateContext());
        var revalidation = await _service.RevalidateAsync(_workspacePath);
        Assert.NotNull(revalidation.Data!.Receipt);

        var report = _service.CompareUpcomingLaunch(revalidation.Data.Receipt!, CreateContext(launchId: "launch-2"));

        Assert.True(report.HasReceipt);
        Assert.False(report.HasDrift);
    }

    /// <summary>
    /// A changed manifest version is reported as drift naming the manifest and both versions.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task CompareUpcomingLaunch_WithChangedManifestVersion_ReportsDrift()
    {
        await _service.RecordLaunchAsync(CreateContext());
        var receipt = await ReadReceiptAsync();

        var report = _service.CompareUpcomingLaunch(receipt, CreateContext(manifestVersion: "2.0"));

        Assert.True(report.HasDrift);
        Assert.Contains(report.DriftedFields, f =>
            f.Contains("1.0.genhub.mod.test") && f.Contains("version changed from 1.0 to 2.0"));
    }

    /// <summary>
    /// A changed game client is reported as drift naming both clients.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task CompareUpcomingLaunch_WithChangedGameClient_ReportsDrift()
    {
        await _service.RecordLaunchAsync(CreateContext());
        var receipt = await ReadReceiptAsync();

        var report = _service.CompareUpcomingLaunch(receipt, CreateContext(gameClientId: "client-2"));

        Assert.True(report.HasDrift);
        Assert.Contains(report.DriftedFields, f => f.Contains("Game client changed from client-1 to client-2"));
    }

    /// <summary>
    /// A changed game type is reported as drift.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task CompareUpcomingLaunch_WithChangedGameType_ReportsDrift()
    {
        await _service.RecordLaunchAsync(CreateContext());
        var receipt = await ReadReceiptAsync();

        var report = _service.CompareUpcomingLaunch(receipt, CreateContext(gameType: GameType.Generals));

        Assert.True(report.HasDrift);
        Assert.Contains(report.DriftedFields, f => f.Contains("Game type changed"));
    }

    /// <summary>
    /// A changed executable path is reported as drift even when the file itself is fine.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task CompareUpcomingLaunch_WithChangedExecutablePath_ReportsDrift()
    {
        await _service.RecordLaunchAsync(CreateContext());
        var receipt = await ReadReceiptAsync();

        var report = _service.CompareUpcomingLaunch(
            receipt, CreateContext(executablePath: Path.Combine(_workspacePath, "otherclient")));

        Assert.True(report.HasDrift);
        Assert.Contains(report.DriftedFields, f => f.Contains("Executable path changed"));
    }

    /// <summary>
    /// A root moved to a different path with identical contents is reported as path drift;
    /// the filesystem fingerprints alone could not tell the roots apart.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task CompareUpcomingLaunch_WithChangedRootPathAndIdenticalContents_ReportsDrift()
    {
        await _service.RecordLaunchAsync(CreateContext());
        var receipt = await ReadReceiptAsync();

        var relocatedRoot = Directory.CreateDirectory(Path.Combine(_root, "retail-copy")).FullName;
        foreach (var archivePath in Directory.GetFiles(_archiveRoot, "*.big"))
        {
            File.Copy(archivePath, Path.Combine(relocatedRoot, Path.GetFileName(archivePath)));
        }

        var report = _service.CompareUpcomingLaunch(receipt, CreateContext(archiveRoot: relocatedRoot));

        Assert.True(report.HasDrift);
        Assert.Contains(report.DriftedFields, f =>
            f.Contains("Archive root path") &&
            f.Contains(RetailArchiveConstants.ZeroHourInstallPathVariable) &&
            f.Contains(relocatedRoot));
    }

    /// <summary>
    /// A changed manifest set is reported per added and removed manifest.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task CompareUpcomingLaunch_WithChangedManifestSet_ReportsDrift()
    {
        await _service.RecordLaunchAsync(CreateContext());
        var receipt = await ReadReceiptAsync();

        var upcoming = CreateContext();
        upcoming.ManifestIds = ["1.0.genhub.mod.other"];

        var report = _service.CompareUpcomingLaunch(receipt, upcoming);

        Assert.True(report.HasDrift);
        Assert.Contains(report.DriftedFields, f =>
            f.Contains("no longer part of the launch") && f.Contains("1.0.genhub.mod.test"));
        Assert.Contains(report.DriftedFields, f =>
            f.Contains("added since the last launch") && f.Contains("1.0.genhub.mod.other"));
    }

    /// <summary>
    /// Recording into a missing workspace fails without throwing.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task RecordLaunchAsync_WithMissingWorkspace_ReturnsFailure()
    {
        var context = CreateContext();
        context.WorkspacePath = Path.Combine(_root, "does-not-exist");

        var result = await _service.RecordLaunchAsync(context);

        Assert.False(result.Success);
        Assert.Contains("Failed to record launch receipt", result.FirstError);
    }

    /// <summary>
    /// Removes the temporary directories.
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort; a leftover temp directory is not worth failing the run over.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Reads the receipt back from the workspace.
    /// </summary>
    /// <returns>The deserialized receipt.</returns>
    private async Task<LaunchReceipt> ReadReceiptAsync()
    {
        var json = await File.ReadAllTextAsync(Path.Combine(_workspacePath, FileTypes.LaunchReceiptFileName));
        var receipt = JsonSerializer.Deserialize<LaunchReceipt>(
            json,
            new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
        Assert.NotNull(receipt);
        return receipt;
    }

    /// <summary>
    /// Creates a context pointing at the fixture workspace, executable and archive root.
    /// </summary>
    /// <param name="launchId">The launch identifier to record.</param>
    /// <param name="gameClientId">The game client identifier.</param>
    /// <param name="gameType">The game type.</param>
    /// <param name="executablePath">The executable path; the fixture executable when null.</param>
    /// <param name="archiveRoot">The archive root; the fixture root when null.</param>
    /// <param name="manifestVersion">The version of the single fixture manifest.</param>
    /// <returns>The context.</returns>
    private LaunchReceiptContext CreateContext(
        string launchId = "launch-1",
        string gameClientId = "client-1",
        GameType gameType = GameType.ZeroHour,
        string? executablePath = null,
        string? archiveRoot = null,
        string manifestVersion = "1.0")
    {
        return new LaunchReceiptContext
        {
            LaunchId = launchId,
            ProfileId = "profile-1",
            GameClientId = gameClientId,
            GameType = gameType,
            WorkspaceId = "profile-1",
            WorkspacePath = _workspacePath,
            ExecutablePath = executablePath ?? _executablePath,
            WorkingDirectory = _workspacePath,
            EnvironmentVariables = new Dictionary<string, string>
            {
                [RetailArchiveConstants.ZeroHourInstallPathVariable] =
                    (archiveRoot ?? _archiveRoot) + Path.DirectorySeparatorChar,
            },
            ManifestIds = ["1.0.genhub.mod.test"],
            ManifestVersions = new Dictionary<string, string> { ["1.0.genhub.mod.test"] = manifestVersion },
        };
    }
}
