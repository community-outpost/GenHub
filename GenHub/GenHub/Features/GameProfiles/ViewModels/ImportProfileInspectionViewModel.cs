using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.GameProfile;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// ViewModel for the rich pre-import profile inspection dialog window.
/// </summary>
[SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Mutates and accesses CommunityToolkit generated observable properties.")]
public partial class ImportProfileInspectionViewModel : ObservableObject
{
    private readonly SharedProfileInspectionResult _inspectionResult;
    private readonly IProfileSharingService _profileSharingService;
    private readonly INotificationService? _notificationService;
    private readonly ILogger<ImportProfileInspectionViewModel> _logger;
    private CancellationTokenSource? _importCts;

    [ObservableProperty]
    private string _profileName = string.Empty;

    [ObservableProperty]
    private string _gameVersion = string.Empty;

    [ObservableProperty]
    private string _publisher = "Community";

    [ObservableProperty]
    private string _themeColor = "#9575CD";

    [ObservableProperty]
    private string? _coverPath;

    [ObservableProperty]
    private string? _iconPath;

    [ObservableProperty]
    private string _commandLineArguments = string.Empty;

    [ObservableProperty]
    private bool _hasNameConflict;

    [ObservableProperty]
    private bool _hasValidGameInstallation;

    [ObservableProperty]
    private SharedInstallationOption? _selectedInstallation;

    [ObservableProperty]
    private bool _includeGameSettings = true;

    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private int _importProgressPercentage;

    [ObservableProperty]
    private string _currentOperationName = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private ObservableCollection<SharedManifestItemViewModel> _manifests = [];

    [ObservableProperty]
    private ObservableCollection<SharedInstallationOption> _compatibleInstallations = [];

    [ObservableProperty]
    private ObservableCollection<string> _securityWarnings = [];

    [ObservableProperty]
    private bool _hasSecurityWarnings;

    [ObservableProperty]
    private long _totalDownloadBytesRequired;

    [ObservableProperty]
    private string _formattedTotalDownloadSize = "0 MB";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMissingDownloads))]
    private int _missingManifestCount;

    [ObservableProperty]
    private int _cachedManifestCount;

    [ObservableProperty]
    private string _actionButtonText = "Import Profile";

    /// <summary>
    /// Gets a value indicating whether there are dependencies that need to be downloaded.
    /// </summary>
    public bool HasMissingDownloads => MissingManifestCount > 0;

    /// <summary>
    /// Event triggered when the dialog requests to close.
    /// </summary>
    public event EventHandler? CloseRequested;

    private static string? SanitizeArtworkPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (path.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        if (Path.IsPathRooted(path) || path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.Contains("://", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        return path;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 MB";
        }

        double mb = bytes / (1024.0 * 1024.0);
        if (mb >= 1024.0)
        {
            return $"{mb / 1024.0:F1} GB";
        }

        return $"{mb:F1} MB";
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ImportProfileInspectionViewModel"/> class.
    /// </summary>
    /// <param name="inspectionResult">The inspection result detailing dependencies and installations.</param>
    /// <param name="profileSharingService">The sharing service instance.</param>
    /// <param name="notificationService">The notification service instance.</param>
    /// <param name="logger">The logger instance.</param>
    public ImportProfileInspectionViewModel(
        SharedProfileInspectionResult inspectionResult,
        IProfileSharingService profileSharingService,
        INotificationService? notificationService,
        ILogger<ImportProfileInspectionViewModel> logger)
    {
        _inspectionResult = inspectionResult ?? throw new ArgumentNullException(nameof(inspectionResult));
        _profileSharingService = profileSharingService ?? throw new ArgumentNullException(nameof(profileSharingService));
        _notificationService = notificationService;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        ProfileName = inspectionResult.SuggestedProfileName;
        HasNameConflict = inspectionResult.HasNameConflict;
        GameVersion = $"{inspectionResult.ProfileMetadata.GameType} {inspectionResult.ProfileMetadata.GameVersion}".Trim();
        Publisher = "Community";
        ThemeColor = !string.IsNullOrEmpty(inspectionResult.ProfileMetadata.ThemeColor)
            ? inspectionResult.ProfileMetadata.ThemeColor
            : "#9575CD";
        CoverPath = SanitizeArtworkPath(inspectionResult.ProfileMetadata.CoverPath);
        IconPath = SanitizeArtworkPath(inspectionResult.ProfileMetadata.IconPath);
        CommandLineArguments = inspectionResult.ProfileMetadata.CommandLineArguments;

        Manifests = new ObservableCollection<SharedManifestItemViewModel>(
            inspectionResult.Manifests.Select(m => new SharedManifestItemViewModel(m)));

        var installOptions = inspectionResult.CompatibleInstallations
            .Select(i => new SharedInstallationOption
            {
                Id = i.Id,
                DisplayName = $"{i.InstallationType} ({i.InstallationPath})",
                InstallationPath = i.InstallationPath,
            })
            .ToList();

        CompatibleInstallations = new ObservableCollection<SharedInstallationOption>(installOptions);
        SelectedInstallation = installOptions.FirstOrDefault(i => i.Id == inspectionResult.MatchedGameInstallationId)
            ?? installOptions.FirstOrDefault();

        HasValidGameInstallation = inspectionResult.HasValidGameInstallation;

        TotalDownloadBytesRequired = inspectionResult.TotalDownloadBytesRequired;
        FormattedTotalDownloadSize = FormatBytes(inspectionResult.TotalDownloadBytesRequired);
        MissingManifestCount = inspectionResult.MissingManifestCount;
        CachedManifestCount = inspectionResult.CachedManifestCount;

        SecurityWarnings = new ObservableCollection<string>(inspectionResult.SecurityWarnings);
        HasSecurityWarnings = inspectionResult.SecurityWarnings.Count > 0;

        ActionButtonText = TotalDownloadBytesRequired > 0
            ? $"Import & Download ({FormattedTotalDownloadSize})"
            : "Import Profile";
    }

    [RelayCommand]
    private async Task ConfirmImportAsync()
    {
        if (string.IsNullOrWhiteSpace(ProfileName))
        {
            SetError("Profile name cannot be empty.");
            return;
        }

        if (ProfileName.Trim().Length > ProfileSharingConstants.MaxProfileNameLength)
        {
            SetError($"Profile name cannot exceed {ProfileSharingConstants.MaxProfileNameLength} characters.");
            return;
        }

        if (SelectedInstallation == null)
        {
            SetError("Please select a valid game installation.");
            return;
        }

        try
        {
            IsImporting = true;
            HasError = false;
            ErrorMessage = string.Empty;
            CurrentOperationName = "Starting import...";
            ImportProgressPercentage = 0;

            _importCts = new CancellationTokenSource();

            var progress = new Progress<ContentAcquisitionProgress>(p =>
            {
                ImportProgressPercentage = (int)Math.Round(p.ProgressPercentage);
                CurrentOperationName = p.CurrentOperation ?? "Processing content...";
            });

            var request = new SharedProfileImportRequest
            {
                Package = _inspectionResult.Package,
                ProfileName = ProfileName.Trim(),
                GameInstallationId = SelectedInstallation.Id,
                WorkspaceStrategy = _inspectionResult.ProfileMetadata.WorkspaceStrategy,
                IncludeGameSettings = IncludeGameSettings,
            };

            var result = await _profileSharingService.ImportSharedProfileAsync(request, progress, _importCts.Token);

            if (result.Success && result.Data != null)
            {
                _logger.LogInformation("Profile {ProfileName} imported successfully.", ProfileName);
                _notificationService?.ShowSuccess("Profile Imported", $"Successfully imported '{ProfileName}'.");
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                SetError(result.FirstError ?? "Failed to import profile.");
            }
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogInformation(ex, "Profile import was cancelled by user.");
            SetError("Import cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during shared profile import.");
            SetError($"Import failed: {ex.Message}");
        }
        finally
        {
            IsImporting = false;
            _importCts?.Dispose();
            _importCts = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _importCts?.Cancel();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SetError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }
}
