using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameProfiles;
using GenHub.Features.Notifications.ViewModels;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// Properties and observable state for the GameProfileSettingsViewModel.
/// </summary>
public partial class GameProfileSettingsViewModel
{
    /// <summary>
    /// Gets or sets the action triggered when the view needs to scroll to a specific section.
    /// </summary>
    public Action<string>? ScrollToSectionRequested { get; set; }

    [ObservableProperty]
    private GeneralSettingsCategory _selectedGeneralCategory = GeneralSettingsCategory.Identity;

    [ObservableProperty]
    private ContentSettingsCategory _selectedContentCategory = ContentSettingsCategory.Selection;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _colorValue = "#5E35B1";

    private ContentType _selectedContentType = ContentType.GameClient;

    /// <summary>
    /// Gets or sets the selected content type for filtering available content.
    /// </summary>
    public ContentType SelectedContentType
    {
        get => _selectedContentType;
        set
        {
            if (SetProperty(ref _selectedContentType, value))
            {
                _ = OnContentTypeChangedAsync();
            }
        }
    }

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private ObservableCollection<ContentDisplayItem> _availableContent = [];

    [ObservableProperty]
    private ObservableCollection<ContentDisplayItem> _availableGameInstallations = [];

    [ObservableProperty]
    private ContentDisplayItem? _selectedGameInstallation;

    [ObservableProperty]
    private ObservableCollection<ContentDisplayItem> _enabledContent = [];

    [ObservableProperty]
    private ObservableCollection<FilterTypeInfo> _visibleFilters = [];

    [ObservableProperty]
    private bool _isInitializing;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _loadingError;

    [ObservableProperty]
    private WorkspaceStrategy _selectedWorkspaceStrategy = WorkspaceConstants.DefaultWorkspaceStrategy;

    [ObservableProperty]
    private string _commandLineArguments = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ProfileInfoItem> _availableCovers = [];

    [ObservableProperty]
    private ProfileInfoItem? _selectedCover;

    [ObservableProperty]
    private ObservableCollection<ProfileInfoItem> _availableGameClients = [];

    [ObservableProperty]
    private ProfileInfoItem? _selectedClient;

    [ObservableProperty]
    private string _formattedSize = string.Empty;

    [ObservableProperty]
    private string _buildDate = string.Empty;

    [ObservableProperty]
    private string _sourceType = string.Empty;

    [ObservableProperty]
    private string _shortcutPath = string.Empty;

    [ObservableProperty]
    private bool _isShortcutPathValid = true;

    [ObservableProperty]
    private string _shortcutStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _useProfileIcon;

    [ObservableProperty]
    private bool _shortcutRunAsAdmin;

    [ObservableProperty]
    private string _shortcutDescription = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ProfileInfoItem> _profileInfos = [];

    [ObservableProperty]
    private ProfileInfoItem? _selectedProfileInfo;

    [ObservableProperty]
    private ObservableCollection<ProfileInfoItem> _availableExecutables = [];

    [ObservableProperty]
    private ProfileInfoItem? _selectedExecutable;

    [ObservableProperty]
    private bool _isExecutableValid = true;

    [ObservableProperty]
    private ObservableCollection<ProfileInfoItem> _availableDataPaths = [];

    [ObservableProperty]
    private ProfileInfoItem? _selectedDataPath;

    [ObservableProperty]
    private bool _isDataPathValid = true;

    [ObservableProperty]
    private bool _runAsAdmin;

    [ObservableProperty]
    private bool _canLaunchGame = true;

    [ObservableProperty]
    private string _iconPath = string.Empty;

    [ObservableProperty]
    private string _coverPath = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ProfileResourceItem> _availableIcons = [];

    [ObservableProperty]
    private ObservableCollection<ProfileResourceItem> _availableCoversForSelection = [];

    [ObservableProperty]
    private ProfileResourceItem? _selectedIcon;

    [ObservableProperty]
    private ProfileResourceItem? _selectedCoverItem;

    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _sourceTypeName = string.Empty;

    [ObservableProperty]
    private string _gameType = string.Empty;

    [ObservableProperty]
    private string _installPath = string.Empty;

    [ObservableProperty]
    private bool _isLoadingContent;

    [ObservableProperty]
    private GameType _gameTypeFilter = Core.Models.Enums.GameType.ZeroHour;

    [ObservableProperty]
    private bool _isAddLocalContentDialogOpen;

    [ObservableProperty]
    private string _localContentName = string.Empty;

    [ObservableProperty]
    private string _localContentDirectoryPath = string.Empty;

    [ObservableProperty]
    private ContentType _selectedLocalContentType = ContentType.Addon;

    [ObservableProperty]
    private GameType _selectedLocalGameType = Core.Models.Enums.GameType.ZeroHour;
}
