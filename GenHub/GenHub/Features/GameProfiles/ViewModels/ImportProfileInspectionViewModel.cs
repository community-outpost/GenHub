using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.GameProfile;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// ViewModel for the rich pre-import profile inspection dialog window.
/// </summary>
[SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Mutates and accesses CommunityToolkit generated observable properties.")]
public sealed partial class ImportProfileInspectionViewModel(
    SharedProfileInspectionResult inspectionResult,
    IProfileSharingService profileSharingService,
    INotificationService? notificationService,
    ILogger<ImportProfileInspectionViewModel> logger) : ObservableObject, IDisposable
{
    private readonly bool _guardsChecked = ValidateArguments(inspectionResult, profileSharingService, logger);
    private CancellationTokenSource? _importCts;
    private Task? _importTask;
    private bool _disposed;

    [ObservableProperty]
    private bool _hasExecutableWarnings = CalculateTotalExecutables(inspectionResult) > 0;

    [ObservableProperty]
    private int _totalExecutableFilesCount = CalculateTotalExecutables(inspectionResult);

    [ObservableProperty]
    private string _profileName = inspectionResult?.SuggestedProfileName ?? string.Empty;

    [ObservableProperty]
    private string _gameVersion = $"{inspectionResult?.ProfileMetadata.GameType} {inspectionResult?.ProfileMetadata.GameVersion}".Trim();

    [ObservableProperty]
    private string _publisher = ProfileSharingConstants.DefaultCommunityPublisherName;

    [ObservableProperty]
    private string _themeColor = !string.IsNullOrEmpty(inspectionResult?.ProfileMetadata.ThemeColor)
        ? inspectionResult.ProfileMetadata.ThemeColor
        : ProfileSharingConstants.DefaultShareAccentColor;

    [ObservableProperty]
    private string? _coverPath = SanitizeArtworkPath(inspectionResult?.ProfileMetadata.CoverPath);

    [ObservableProperty]
    private string? _iconPath = SanitizeArtworkPath(inspectionResult?.ProfileMetadata.IconPath);

    [ObservableProperty]
    private string _commandLineArguments = inspectionResult?.ProfileMetadata.CommandLineArguments ?? string.Empty;

    [ObservableProperty]
    private bool _hasNameConflict = inspectionResult?.HasNameConflict ?? false;

    [ObservableProperty]
    private bool _hasValidGameInstallation = inspectionResult?.HasValidGameInstallation ?? false;

    [ObservableProperty]
    private SharedInstallationOption? _selectedInstallation = inspectionResult != null ? CreateSelectedInstallation(inspectionResult) : null;

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
    private ObservableCollection<SharedManifestItemViewModel> _manifests = new(
        inspectionResult?.Manifests.Select(m => new SharedManifestItemViewModel(m)) ?? []);

    [ObservableProperty]
    private ObservableCollection<SharedInstallationOption> _compatibleInstallations = inspectionResult != null ? CreateInstallationOptions(inspectionResult) : [];

    [ObservableProperty]
    private ObservableCollection<string> _securityWarnings = inspectionResult != null ? BuildSecurityWarnings(inspectionResult, CalculateTotalExecutables(inspectionResult)) : [];

    [ObservableProperty]
    private bool _hasSecurityWarnings = (inspectionResult?.SecurityWarnings.Count > 0) || (inspectionResult != null && CalculateTotalExecutables(inspectionResult) > 0);

    [ObservableProperty]
    private long _totalDownloadBytesRequired = inspectionResult?.TotalDownloadBytesRequired ?? 0;

    [ObservableProperty]
    private string _formattedTotalDownloadSize = ByteFormatHelper.FormatBytes(inspectionResult?.TotalDownloadBytesRequired ?? 0);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMissingDownloads))]
    private int _missingManifestCount = inspectionResult?.MissingManifestCount ?? 0;

    [ObservableProperty]
    private int _cachedManifestCount = inspectionResult?.CachedManifestCount ?? 0;

    [ObservableProperty]
    private string _actionButtonText = (inspectionResult?.TotalDownloadBytesRequired ?? 0) > 0
        ? $"Import & Download ({ByteFormatHelper.FormatBytes(inspectionResult?.TotalDownloadBytesRequired ?? 0)})"
        : "Import Profile";

    private static bool ValidateArguments(
        SharedProfileInspectionResult inspectionResult,
        IProfileSharingService profileSharingService,
        ILogger<ImportProfileInspectionViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(inspectionResult);
        ArgumentNullException.ThrowIfNull(profileSharingService);
        ArgumentNullException.ThrowIfNull(logger);
        return true;
    }

    /// <summary>
    /// Event triggered when the dialog requests to close.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Gets a value indicating whether there are dependencies that need to be downloaded.
    /// </summary>
    public bool HasMissingDownloads => MissingManifestCount > 0;

    /// <summary>
    /// Releases unmanaged and managed resources used by the view model.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var cts = _importCts;
        _importCts = null;
        if (cts != null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // CancellationTokenSource was already disposed during cancellation.
            }

            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        if (_importTask != null)
                        {
                            await _importTask.ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Suppressed exception while awaiting cancelled import task during disposal.");
                    }
                    finally
                    {
                        cts.Dispose();
                    }
                },
                CancellationToken.None);
        }

        GC.SuppressFinalize(this);
    }

    private static string? SanitizeArtworkPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string trimmed = path.Trim();

        // Built-in assets and Avalonia resources are safe and portable across all GenHub installations
        if (trimmed.StartsWith("avares://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("/Assets/", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        if (Path.IsPathRooted(trimmed) || trimmed.StartsWith(@"\\", StringComparison.Ordinal) ||
            trimmed.Contains("://", StringComparison.Ordinal) || trimmed.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        return trimmed;
    }

    private static int CalculateTotalExecutables(SharedProfileInspectionResult result) =>
        result.Manifests.Sum(m => m.Files?.Count(f =>
            !string.IsNullOrWhiteSpace(f.RelativePath) &&
            ProfileSharingConstants.ExecutableFileExtensions.Contains(Path.GetExtension(f.RelativePath))) ?? 0);

    private static ObservableCollection<SharedInstallationOption> CreateInstallationOptions(SharedProfileInspectionResult inspection) =>
        new(inspection.CompatibleInstallations.Select(i => new SharedInstallationOption
        {
            Id = i.Id,
            DisplayName = $"{i.InstallationType} ({i.InstallationPath})",
            InstallationPath = i.InstallationPath,
        }));

    private static SharedInstallationOption? CreateSelectedInstallation(SharedProfileInspectionResult inspection)
    {
        var matched = inspection.CompatibleInstallations.FirstOrDefault(i => i.Id == inspection.MatchedGameInstallationId)
            ?? inspection.CompatibleInstallations.FirstOrDefault();

        return matched == null
            ? null
            : new SharedInstallationOption
            {
                Id = matched.Id,
                DisplayName = $"{matched.InstallationType} ({matched.InstallationPath})",
                InstallationPath = matched.InstallationPath,
            };
    }

    private static ObservableCollection<string> BuildSecurityWarnings(SharedProfileInspectionResult result, int totalExecutables)
    {
        var warnings = new List<string>(result.SecurityWarnings);
        if (totalExecutables > 0)
        {
            var extList = string.Join(", ", ProfileSharingConstants.ExecutableFileExtensions);
            warnings.Insert(0, $"Executable binaries detected ({totalExecutables} file(s) ending in {extList}). Ensure you trust the author before running.");
        }

        return new ObservableCollection<string>(warnings);
    }

    [RelayCommand]
    private async Task ConfirmImportAsync()
    {
        if (_disposed)
        {
            return;
        }

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
            var cts = _importCts;

            var progress = new Progress<ContentAcquisitionProgress>(p =>
            {
                ImportProgressPercentage = (int)Math.Round(p.ProgressPercentage);
                CurrentOperationName = p.CurrentOperation ?? "Processing content...";
            });

            var request = new SharedProfileImportRequest
            {
                Package = inspectionResult.Package,
                ProfileName = ProfileName.Trim(),
                GameInstallationId = SelectedInstallation.Id,
                WorkspaceStrategy = inspectionResult.ProfileMetadata.WorkspaceStrategy,
                IncludeGameSettings = IncludeGameSettings,
            };

            var importTask = profileSharingService.ImportSharedProfileAsync(request, progress, cts.Token);
            _importTask = importTask;
            var result = await importTask;

            if (result.Success && result.Data != null)
            {
                logger.LogInformation("Profile {ProfileName} imported successfully.", ProfileName);
                notificationService?.ShowSuccess("Profile Imported", $"Successfully imported '{ProfileName}'.");
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                SetError(result.FirstError ?? "Failed to import profile.");
            }
        }
        catch (OperationCanceledException ex)
        {
            logger.LogInformation(ex, "Profile import was cancelled by user.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during shared profile import.");
            SetError($"Import failed: {ex.Message}");
        }
        finally
        {
            IsImporting = false;
            if (_importCts != null)
            {
                _importCts.Dispose();
                _importCts = null;
            }
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
        notificationService?.ShowError("Profile Import Failed", message);
    }
}
