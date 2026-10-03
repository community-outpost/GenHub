using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;
using GenHub.Core.Models.Validation;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Storage.Services;
using GenHub.Features.Workspace;
using GenHub.Features.Workspace.Strategies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Workspace;

/// <summary>
/// Tests that workspace preparation skips a file whose source is missing and reports it.
/// </summary>
public sealed class WorkspaceSkippedSourceFilesTests : IDisposable
{
    private const string SharedPath = "Data/INI/GameData.ini";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString("N"));
    private readonly Mock<IFileOperationsService> _fileOperations = new();

    /// <summary>
    /// When the winning manifest's source for a shared path is missing, every strategy skips the
    /// file, does not fall back to the losing copy, and records the skip.
    /// </summary>
    /// <param name="strategyType">The strategy under test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Theory]
    [InlineData(WorkspaceStrategy.HybridCopySymlink)]
    [InlineData(WorkspaceStrategy.SymlinkOnly)]
    [InlineData(WorkspaceStrategy.HardLink)]
    [InlineData(WorkspaceStrategy.FullCopy)]
    public async Task PrepareAsync_WhenWinningSourceIsMissing_SkipsAndRecordsTheFileAsync(WorkspaceStrategy strategyType)
    {
        var installDir = Directory.CreateDirectory(Path.Combine(_root, "Install")).FullName;
        var losingSource = Path.Combine(installDir, "base.ini");
        File.WriteAllText(losingSource, "base");
        var missingWinningSource = Path.Combine(installDir, "missing-mod.ini");
        var configuration = new WorkspaceConfiguration
        {
            Id = Guid.NewGuid().ToString("N"),
            Strategy = strategyType,
            WorkspaceRootPath = Path.Combine(_root, "Workspaces"),
            BaseInstallationPath = installDir,
            GameClient = new GameClient { Id = "test" },
            Manifests =
            [
                CreateManifest("1.0.test.gameinstallation.base", ContentType.GameInstallation, losingSource),
                CreateManifest("1.0.test.mod.winner", ContentType.Mod, missingWinningSource),
            ],
        };

        var result = await CreateStrategy(strategyType).PrepareAsync(configuration, null, CancellationToken.None);

        Assert.True(result.IsPrepared);
        Assert.Equal(SharedPath, Assert.Single(configuration.SkippedSourceFiles));
        Assert.DoesNotContain(
            _fileOperations.Invocations,
            invocation => invocation.Arguments.OfType<string>().Any(argument => argument == losingSource));
    }

    /// <summary>
    /// The workspace manager turns recorded skips into warnings on the workspace and shows one
    /// notification that names the skipped file, while preparation still succeeds.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task PrepareWorkspaceAsync_WithSkippedFile_ReportsWarningAndNotifiesAsync()
    {
        var notifications = new Mock<INotificationService>();
        var (manager, strategy) = CreateManager(notifications.Object);
        strategy.Setup(s => s.PrepareAsync(It.IsAny<WorkspaceConfiguration>(), It.IsAny<IProgress<WorkspacePreparationProgress>>(), It.IsAny<CancellationToken>()))
            .Callback<WorkspaceConfiguration, IProgress<WorkspacePreparationProgress>?, CancellationToken>((config, _, _) => config.RecordSkippedSourceFile(SharedPath))
            .ReturnsAsync(new WorkspaceInfo { Id = "skipped-workspace", IsPrepared = true, WorkspacePath = Path.Combine(_root, "skipped-workspace") });

        var result = await manager.PrepareWorkspaceAsync(CreateManagerConfiguration());

        Assert.True(result.Success, result.FirstError);
        var issue = Assert.Single(result.Data!.ValidationIssues, i => i.IssueType == ValidationIssueType.MissingFile);
        Assert.Equal(ValidationSeverity.Warning, issue.Severity);
        Assert.Contains(SharedPath, issue.Message);
        notifications.Verify(
            n => n.ShowWarning(It.IsAny<string>(), It.Is<string>(m => m.Contains(SharedPath)), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// The workspace manager shows no notification when nothing was skipped.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task PrepareWorkspaceAsync_WithoutSkippedFiles_DoesNotNotifyAsync()
    {
        var notifications = new Mock<INotificationService>();
        var (manager, strategy) = CreateManager(notifications.Object);
        strategy.Setup(s => s.PrepareAsync(It.IsAny<WorkspaceConfiguration>(), It.IsAny<IProgress<WorkspacePreparationProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceInfo { Id = "skipped-workspace", IsPrepared = true, WorkspacePath = Path.Combine(_root, "skipped-workspace") });

        var result = await manager.PrepareWorkspaceAsync(CreateManagerConfiguration());

        Assert.True(result.Success, result.FirstError);
        Assert.DoesNotContain(result.Data!.ValidationIssues, i => i.IssueType == ValidationIssueType.MissingFile);
        notifications.Verify(
            n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// A configuration prepared again does not report skips left over from an earlier preparation.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task PrepareWorkspaceAsync_WithStaleSkipFromEarlierPreparation_DoesNotReportItAsync()
    {
        var notifications = new Mock<INotificationService>();
        var (manager, strategy) = CreateManager(notifications.Object);
        strategy.Setup(s => s.PrepareAsync(It.IsAny<WorkspaceConfiguration>(), It.IsAny<IProgress<WorkspacePreparationProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceInfo { Id = "skipped-workspace", IsPrepared = true, WorkspacePath = Path.Combine(_root, "skipped-workspace") });
        var configuration = CreateManagerConfiguration();
        configuration.RecordSkippedSourceFile(SharedPath);

        var result = await manager.PrepareWorkspaceAsync(configuration);

        Assert.True(result.Success, result.FirstError);
        Assert.DoesNotContain(result.Data!.ValidationIssues, i => i.IssueType == ValidationIssueType.MissingFile);
        notifications.Verify(
            n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// The hard-link strategy records a source that disappears after the first existence check.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task HardLinkPrepareAsync_WhenSourceDisappearsBeforeLinking_RecordsTheSkipAsync()
    {
        var installDir = Directory.CreateDirectory(Path.Combine(_root, "Install")).FullName;
        var source = Path.Combine(installDir, "mod.ini");
        File.WriteAllText(source, "mod");
        _fileOperations
            .Setup(f => f.CreateHardLinkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("The source file does not exist."));
        var configuration = new WorkspaceConfiguration
        {
            Id = Guid.NewGuid().ToString("N"),
            Strategy = WorkspaceStrategy.HardLink,
            WorkspaceRootPath = Path.Combine(_root, "Workspaces"),
            BaseInstallationPath = installDir,
            GameClient = new GameClient { Id = "test" },
            Manifests = [CreateManifest("1.0.test.mod.winner", ContentType.Mod, source)],
        };

        var result = await CreateStrategy(WorkspaceStrategy.HardLink).PrepareAsync(configuration, null, CancellationToken.None);

        Assert.True(result.IsPrepared);
        Assert.Equal(SharedPath, Assert.Single(configuration.SkippedSourceFiles));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
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

    private static ContentManifest CreateManifest(string id, ContentType type, string source) => new()
    {
        Id = ManifestId.Create(id),
        ContentType = type,
        Files = [new() { RelativePath = SharedPath, SourcePath = source, Size = 4, SourceType = ContentSourceType.GameInstallation }],
    };

    private (WorkspaceManager Manager, Mock<IWorkspaceStrategy> Strategy) CreateManager(INotificationService notifications)
    {
        Directory.CreateDirectory(_root);
        var configProvider = new Mock<IConfigurationProviderService>();
        configProvider.Setup(c => c.GetApplicationDataPath()).Returns(_root);
        var strategy = new Mock<IWorkspaceStrategy>();
        strategy.Setup(s => s.Name).Returns("TestStrategy");
        strategy.Setup(s => s.CanHandle(It.IsAny<WorkspaceConfiguration>())).Returns(true);
        var validator = new Mock<IWorkspaceValidator>();
        var success = new ValidationResult("skipped-workspace", []);
        validator.Setup(v => v.ValidateConfigurationAsync(It.IsAny<WorkspaceConfiguration>(), It.IsAny<CancellationToken>())).ReturnsAsync(success);
        validator.Setup(v => v.ValidatePrerequisitesAsync(It.IsAny<IWorkspaceStrategy>(), It.IsAny<WorkspaceConfiguration>(), It.IsAny<CancellationToken>())).ReturnsAsync(success);
        var casConfig = new Mock<IOptions<CasConfiguration>>();
        casConfig.Setup(c => c.Value).Returns(new CasConfiguration { CasRootPath = Path.Combine(_root, "cas") });
        var manager = new WorkspaceManager(
            [strategy.Object],
            configProvider.Object,
            new Mock<ILogger<WorkspaceManager>>().Object,
            new CasReferenceTracker(casConfig.Object, new Mock<ILogger<CasReferenceTracker>>().Object),
            validator.Object,
            new WorkspaceReconciler(new Mock<ILogger<WorkspaceReconciler>>().Object, new Mock<IFileOperationsService>().Object),
            notificationService: notifications);
        return (manager, strategy);
    }

    private WorkspaceConfiguration CreateManagerConfiguration() => new()
    {
        Id = "skipped-workspace",
        Strategy = WorkspaceStrategy.HardLink,
        Manifests = [CreateManifest("1.0.test.mod.winner", ContentType.Mod, Path.Combine(_root, "missing.ini"))],
        BaseInstallationPath = _root,
        WorkspaceRootPath = _root,
        ValidateAfterPreparation = false,
    };

    private IWorkspaceStrategy CreateStrategy(WorkspaceStrategy strategyType) => strategyType switch
    {
        WorkspaceStrategy.FullCopy => new FullCopyStrategy(_fileOperations.Object, new Mock<ILogger<FullCopyStrategy>>().Object),
        WorkspaceStrategy.SymlinkOnly => new SymlinkOnlyStrategy(_fileOperations.Object, new Mock<ILogger<SymlinkOnlyStrategy>>().Object),
        WorkspaceStrategy.HybridCopySymlink => new HybridCopySymlinkStrategy(_fileOperations.Object, new Mock<ILogger<HybridCopySymlinkStrategy>>().Object),
        WorkspaceStrategy.HardLink => new HardLinkStrategy(_fileOperations.Object, new Mock<ILogger<HardLinkStrategy>>().Object),
        _ => throw new ArgumentException($"Unknown strategy type: {strategyType}"),
    };
}
