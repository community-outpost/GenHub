using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Notifications;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.Services;
using GenHub.Features.GameProfiles.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Collections.ObjectModel;
using ContentType = GenHub.Core.Models.Enums.ContentType;
using CoreContentDisplayItem = GenHub.Core.Models.Content.ContentDisplayItem;
using ViewModelContentDisplayItem = GenHub.Features.GameProfiles.ViewModels.ContentDisplayItem;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels;

/// <summary>
/// Tests that the warning shown after enabling content uses the shared dependency validator.
/// </summary>
public sealed class GameProfileSettingsViewModelDependencyWarningTests
{
    private const string WarningTitle = "Dependency Warning";
    private const string ControlBarProId = "1.10.communityoutpost.addon.cbpr";
    private const string InstallationId = "1.104.steam.gameinstallation.zerohour";

    private readonly Dictionary<string, ContentManifest> _manifests = new(StringComparer.OrdinalIgnoreCase);
    private readonly GameProfileSettingsViewModel _viewModel;
    private readonly TaskCompletionSource<NotificationMessage> _warning = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileSettingsViewModelDependencyWarningTests"/> class.
    /// </summary>
    public GameProfileSettingsViewModelDependencyWarningTests()
    {
        AddManifest(new ContentManifest
        {
            Id = ManifestId.Create(InstallationId),
            Name = "Zero Hour",
            ContentType = ContentType.GameInstallation,
            TargetGame = GameType.ZeroHour,
        });

        var pool = new Mock<IContentManifestPool>();
        pool.Setup(x => x.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManifestId id, CancellationToken _) =>
                OperationResult<ContentManifest?>.CreateSuccess(_manifests.GetValueOrDefault(id.Value)));
        var loader = new Mock<IProfileContentLoader>();
        loader.Setup(x => x.LoadAvailableContentAsync(It.IsAny<ContentType>(), It.IsAny<ObservableCollection<CoreContentDisplayItem>>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([]);
        loader.Setup(x => x.LoadAvailableGameInstallationsAsync()).ReturnsAsync([]);

        _viewModel = new GameProfileSettingsViewModel(
            Mock.Of<IGameProfileManager>(),
            Mock.Of<IGameSettingsService>(),
            Mock.Of<IConfigurationProviderService>(),
            loader.Object,
            null,
            Mock.Of<INotificationService>(),
            pool.Object,
            null,
            null,
            null,
            null,
            NullLogger<GameProfileSettingsViewModel>.Instance,
            NullLogger<GameSettingsViewModel>.Instance,
            dependencyValidator: new ContentDependencyValidator(pool.Object, NullLogger<ContentDependencyValidator>.Instance));
        _viewModel.LocalNotifications.Subscribe(new WarningObserver(_warning));
        _viewModel.EnabledContent.Add(CreateItem(InstallationId, "Zero Hour", ContentType.GameInstallation, isEnabled: true));
    }

    /// <summary>
    /// Enabling Control Bar Pro without GenTool warns by display name, without raw IDs.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task EnableContent_ControlBarProWithoutGenTool_WarnsByNameAsync()
    {
        AddControlBarPro();

        var warning = await EnableAndWaitForWarningAsync(CreateItem(ControlBarProId, "Control Bar Pro (ExiLe)", ContentType.Addon));

        Assert.Equal(WarningTitle, warning.Title);
        Assert.Contains("GenTool", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("communityoutpost", warning.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A declared conflict between two enabled items is still reported.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task EnableContent_ConflictingItems_WarnsAboutTheConflictAsync()
    {
        const string otherId = "1.0.examplepublisher.addon.otherbar";
        const string conflictingId = "1.0.examplepublisher.addon.conflictbar";
        AddManifest(new ContentManifest { Id = ManifestId.Create(otherId), Name = "Other Bar", ContentType = ContentType.Addon });
        AddManifest(new ContentManifest
        {
            Id = ManifestId.Create(conflictingId),
            Name = "Conflict Bar",
            ContentType = ContentType.Addon,
            Dependencies =
            [
                new ContentDependency
                {
                    Id = ManifestId.Create(otherId),
                    Name = "Other Bar",
                    DependencyType = ContentType.Addon,
                    IsOptional = true,
                    InstallBehavior = DependencyInstallBehavior.Optional,
                    ConflictsWith = [ManifestId.Create(otherId)],
                },
            ],
        });
        _viewModel.EnabledContent.Add(CreateItem(otherId, "Other Bar", ContentType.Addon, isEnabled: true));

        var warning = await EnableAndWaitForWarningAsync(CreateItem(conflictingId, "Conflict Bar", ContentType.Addon));

        Assert.Contains("'Conflict Bar' conflicts with 'Other Bar'", warning.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A duplicate enabled ID does not break the warning.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task EnableContent_DuplicateEnabledId_StillWarnsAsync()
    {
        AddControlBarPro();
        _viewModel.EnabledContent.Add(CreateItem(InstallationId, "Zero Hour", ContentType.GameInstallation, isEnabled: true));

        var warning = await EnableAndWaitForWarningAsync(CreateItem(ControlBarProId, "Control Bar Pro (ExiLe)", ContentType.Addon));

        Assert.Contains("GenTool", warning.Message, StringComparison.Ordinal);
    }

    private static ViewModelContentDisplayItem CreateItem(string id, string name, ContentType type, bool isEnabled = false) => new()
    {
        ManifestId = ManifestId.Create(id),
        DisplayName = name,
        ContentType = type,
        GameType = GameType.ZeroHour,
        InstallationType = GameInstallationType.Unknown,
        IsEnabled = isEnabled,
    };

    private void AddControlBarPro() => AddManifest(new ContentManifest
    {
        Id = ManifestId.Create(ControlBarProId),
        Name = "Control Bar Pro (ExiLe)",
        ContentType = ContentType.Addon,
        TargetGame = GameType.ZeroHour,
        Publisher = new PublisherInfo { PublisherType = "communityoutpost" },
        Dependencies = GenPatcherDependencyBuilder.GetDependencies("cbpr", GenPatcherContentRegistry.GetMetadata("cbpr"))
            .Where(dependency => dependency.Name == "GenTool" || dependency.DependencyType == ContentType.GameInstallation)
            .ToList(),
    });

    private void AddManifest(ContentManifest manifest) => _manifests[manifest.Id.Value] = manifest;

    private async Task<NotificationMessage> EnableAndWaitForWarningAsync(ViewModelContentDisplayItem item)
    {
        _viewModel.AvailableContent.Add(item);
        _viewModel.EnableContentCommand.Execute(item);
        return await _warning.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class WarningObserver(TaskCompletionSource<NotificationMessage> warning) : IObserver<NotificationMessage>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(NotificationMessage value)
        {
            if (value.Title == WarningTitle)
            {
                warning.TrySetResult(value);
            }
        }
    }
}
