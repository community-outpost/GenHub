using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Storage.Services;
using GenHub.Features.Workspace;
using GenHub.Features.Workspace.Strategies;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace GenHub.Tests.Core.Features.Workspace;

/// <summary>
/// Tests for workspace synchronization functionality.
/// </summary>
public class WorkspaceSyncTests
{
    private readonly WorkspaceManager _workspaceManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceSyncTests"/> class.
    /// </summary>
    public WorkspaceSyncTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString());
        _appDataPath = Path.Combine(_tempPath, "AppData");
        Directory.CreateDirectory(_appDataPath);

        _configProviderMock = new Mock<IConfigurationProviderService>();
        _configProviderMock.Setup(x => x.GetApplicationDataPath()).Returns(_appDataPath);

        _validatorMock = new Mock<IWorkspaceValidator>();
        _validatorMock.Setup(x => x.ValidateConfigurationAsync(It.IsAny<WorkspaceConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", null));
        _validatorMock.Setup(x => x.ValidatePrerequisitesAsync(It.IsAny<IWorkspaceStrategy>(), It.IsAny<WorkspaceConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", null));
        _validatorMock.Setup(x => x.ValidateWorkspaceAsync(It.IsAny<WorkspaceInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ValidationResult>.CreateSuccess(new ValidationResult("test", null)));

        _fileOperationsMock = new Mock<IFileOperationsService>();
        List<IWorkspaceStrategy> strategies =
        [

            // Use a simplified strategy for testing that just creates a file indicating content
            new TestStrategy(_fileOperationsMock.Object),
        ];

        // We need a real CasReferenceTracker for the manager constructor
        var casConfig = new CasConfiguration { CasRootPath = Path.Combine(_tempPath, "CAS") };
        var optionsMock = new Mock<IOptions<CasConfiguration>>();
        optionsMock.Setup(x => x.Value).Returns(casConfig);
        var casTracker = new CasReferenceTracker(optionsMock.Object, NullLogger<CasReferenceTracker>.Instance);

        var reconciler = new WorkspaceReconciler(NullLogger<WorkspaceReconciler>.Instance, _fileOperationsMock.Object);

        _workspaceManager = new WorkspaceManager(
            strategies,
            _configProviderMock.Object,
            NullLogger<WorkspaceManager>.Instance,
            casTracker,
            _validatorMock.Object,
            reconciler);
    }

    private readonly Mock<IConfigurationProviderService> _configProviderMock;
    private readonly Mock<IWorkspaceValidator> _validatorMock;
    private readonly Mock<IFileOperationsService> _fileOperationsMock;
    private readonly string _tempPath;
    private readonly string _appDataPath;

    /// <summary>
    /// Should sync correctly when switching content.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PrepareWorkspace_SwitchingContent_ShouldSyncCorrectly()
    {
        // Arrange
        var profileId = "profile-1";
        var workspaceRoot = Path.Combine(_tempPath, "Workspaces");
        var baseInstall = Path.Combine(_tempPath, "BaseInstall");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(baseInstall);

        // Content A
        var manifestA = new ContentManifest
        {
            Id = ManifestId.Create("1.0.local.mod.contenta"),
            Name = "Content A",
            ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
            Files = [new() { RelativePath = "A.txt", SourceType = ContentSourceType.LocalFile }],
        };

        // Content B
        var manifestB = new ContentManifest
        {
            Id = ManifestId.Create("1.0.local.mod.contentb"),
            Name = "Content B",
            ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
            Files =
            [
                new() { RelativePath = "B.txt", SourceType = ContentSourceType.LocalFile },
                new() { RelativePath = "Orphan.txt", SourceType = ContentSourceType.LocalFile },
            ],
        };

        // 1. Prepare with Content A
        var configA = new WorkspaceConfiguration
        {
            Id = profileId,
            Manifests = [manifestA],
            GameClient = new() { Id = "gc1" },
            WorkspaceRootPath = workspaceRoot,
            BaseInstallationPath = baseInstall,
            Strategy = WorkspaceConstants.DefaultWorkspaceStrategy,
        };

        var resultA = await _workspaceManager.PrepareWorkspaceAsync(configA);
        Assert.True(resultA.Success);
        Assert.Contains(manifestA.Id.Value, resultA.Data.ManifestIds);

        // Verify 'A.txt' exists (simulated by strategy)
        var workspacePath = resultA.Data.WorkspacePath;
        Assert.True(File.Exists(Path.Combine(workspacePath, "A.txt")));

        // 2. Switch to Content B (ForceRecreate = false, rely on change detection)
        var configB = new WorkspaceConfiguration
        {
            Id = profileId, // SAME ID
            Manifests = [manifestB],
            GameClient = new() { Id = "gc1" },
            WorkspaceRootPath = workspaceRoot,
            BaseInstallationPath = baseInstall,
            Strategy = WorkspaceConstants.DefaultWorkspaceStrategy,
        };

        var resultB = await _workspaceManager.PrepareWorkspaceAsync(configB);
        Assert.True(resultB.Success);
        Assert.Contains(manifestB.Id.Value, resultB.Data.ManifestIds);
        Assert.DoesNotContain(manifestA.Id.Value, resultB.Data.ManifestIds);

        // Verify 'B.txt' exists and 'A.txt' is gone (recreation implied)
        Assert.True(File.Exists(Path.Combine(workspacePath, "B.txt")));
        Assert.False(File.Exists(Path.Combine(workspacePath, "A.txt")));

        // 3. Switch BACK to Content A
        // This is a critical step. Does it detect the change back to A?
        var resultA2 = await _workspaceManager.PrepareWorkspaceAsync(configA);
        Assert.True(resultA2.Success);
        Assert.Contains(manifestA.Id.Value, resultA2.Data.ManifestIds);

        // Verify 'A.txt' is back
        Assert.True(File.Exists(Path.Combine(workspacePath, "A.txt")));
        Assert.False(File.Exists(Path.Combine(workspacePath, "B.txt")));
        Assert.False(File.Exists(Path.Combine(workspacePath, "Orphan.txt"))); // Should be gone if ForceRecreate worked
    }

    private class TestStrategy(IFileOperationsService fileOps) : WorkspaceStrategyBase<TestStrategy>(fileOps, NullLogger<TestStrategy>.Instance)
    {
        public override string Name => "Test";

        public override string Description => "Test Strategy";

        public override bool RequiresAdminRights => false;

        public override bool RequiresSameVolume => false;

        public override bool CanHandle(WorkspaceConfiguration configuration) => true;

        public override long EstimateDiskUsage(WorkspaceConfiguration configuration) => 0;

        public override Task<WorkspaceInfo> PrepareAsync(WorkspaceConfiguration configuration, IProgress<WorkspacePreparationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var workspacePath = Path.Combine(configuration.WorkspaceRootPath, configuration.Id);

            if (configuration.ForceRecreate)
            {
                // Clean directory only when forced
                if (Directory.Exists(workspacePath))
                {
                    Directory.Delete(workspacePath, true);
                    Directory.CreateDirectory(workspacePath);
                }
            }
            else
            {
                if (!Directory.Exists(workspacePath))
                {
                    Directory.CreateDirectory(workspacePath);
                }
                else
                {
                    // Basic sync: Remove files not in the new manifest
                    var allowedFiles = configuration.Manifests
                        .SelectMany(m => m.Files)
                        .Select(f => Path.Combine(workspacePath, f.RelativePath))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    foreach (var file in Directory.GetFiles(workspacePath, "*", SearchOption.AllDirectories))
                    {
                        if (!allowedFiles.Contains(file))
                        {
                            File.Delete(file);
                        }
                    }
                }
            }

            // Create files based on manifest to simulate content
            foreach (var m in configuration.Manifests)
            {
                foreach (var f in m.Files)
                {
                    var filePath = Path.Combine(workspacePath, f.RelativePath);
                    var dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(filePath, "content");
                }
            }

            return Task.FromResult(new WorkspaceInfo
            {
                Id = configuration.Id,
                WorkspacePath = workspacePath,
                ManifestIds = [.. configuration.Manifests.Select(m => m.Id.Value)],
                FileCount = configuration.Manifests.Sum(m => m.Files.Count),
                IsPrepared = true,
                IsValid = true,
            });
        }

        protected override Task CreateCasLinkAsync(string hash, string targetPath, GenHub.Core.Models.Enums.ContentType? contentType, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
