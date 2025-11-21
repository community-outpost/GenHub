using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Common.ViewModels;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Models.GameProfile;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// ViewModel for a single game profile item.
/// </summary>
public partial class GameProfileItemViewModel : ViewModelBase
{
    /// <summary>
    /// Gets or sets the name of the game profile.
    /// </summary>
    [ObservableProperty]
    private string _name;

    /// <summary>
    /// Gets or sets the icon path.
    /// </summary>
    [ObservableProperty]
    private string _iconPath;

    /// <summary>
    /// Gets or sets the cover path.
    /// </summary>
    [ObservableProperty]
    private string _coverPath;

    /// <summary>
    /// Gets or sets the version.
    /// </summary>
    [ObservableProperty]
    private string _version;

    /// <summary>
    /// Gets or sets the executable path.
    /// </summary>
    [ObservableProperty]
    private string _executablePath;

    /// <summary>
    /// Gets or sets the color value.
    /// </summary>
    [ObservableProperty]
    private string? _colorValue;

    /// <summary>
    /// Gets or sets the cover image path.
    /// </summary>
    [ObservableProperty]
    private string? _coverImagePath;

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    [ObservableProperty]
    private string? _description;

    /// <summary>
    /// Gets or sets the version ID.
    /// </summary>
    [ObservableProperty]
    private string? _versionId;

    /// <summary>
    /// Gets or sets the source type name.
    /// </summary>
    [ObservableProperty]
    private string? _sourceTypeName;

    /// <summary>
    /// Gets or sets a value indicating whether workflow info is present.
    /// </summary>
    [ObservableProperty]
    private bool _hasWorkflowInfo;

    /// <summary>
    /// Gets or sets the workflow number.
    /// </summary>
    [ObservableProperty]
    private int _workflowNumber;

    /// <summary>
    /// Gets or sets the pull request number.
    /// </summary>
    [ObservableProperty]
    private int _pullRequestNumber;

    /// <summary>
    /// Gets or sets the commit SHA.
    /// </summary>
    [ObservableProperty]
    private string? _commitSha;

    /// <summary>
    /// Gets or sets the short commit SHA.
    /// </summary>
    [ObservableProperty]
    private string? _shortCommitSha;

    /// <summary>
    /// Gets or sets the build info.
    /// </summary>
    [ObservableProperty]
    private string? _buildInfo;

    /// <summary>
    /// Gets or sets the display compiler.
    /// </summary>
    [ObservableProperty]
    private string? _displayCompiler;

    /// <summary>
    /// Gets or sets the display configuration.
    /// </summary>
    [ObservableProperty]
    private string? _displayConfiguration;

    /// <summary>
    /// Gets or sets the build preset.
    /// </summary>
    [ObservableProperty]
    private string? _buildPreset;

    /// <summary>
    /// Gets or sets a value indicating whether to run as administrator.
    /// </summary>
    [ObservableProperty]
    private bool _runAsAdmin;

    /// <summary>
    /// Gets or sets the command line arguments.
    /// </summary>
    [ObservableProperty]
    private string? _commandLineArguments;

    /// <summary>
    /// Gets or sets the launch command.
    /// </summary>
    [ObservableProperty]
    private string? _launchCommand;

    /// <summary>
    /// Gets or sets the content type.
    /// </summary>
    [ObservableProperty]
    private string? _contentType;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileItemViewModel"/> class.
    /// </summary>
    /// <param name="profile">The game profile.</param>
    /// <param name="iconPath">The icon path.</param>
    /// <param name="coverPath">The cover path.</param>
    public GameProfileItemViewModel(IGameProfile profile, string iconPath, string coverPath)
    {
        _name = profile.Name;
        _iconPath = iconPath;
        _coverPath = coverPath;
        _version = profile.Version;
        _executablePath = profile.ExecutablePath;

        // Extract version and publisher info from enabled content manifest IDs (prioritize GameInstallation manifests)
        if (profile is GameProfile gameProfile)
        {
            // First try to get info from enabled GameInstallation manifests (look for "gameinstallation" content type)
            var installationManifestId = gameProfile.EnabledContentIds?.FirstOrDefault(id => id.Contains("gameinstallation"));
            if (!string.IsNullOrEmpty(installationManifestId))
            {
                try
                {
                    var segments = installationManifestId.Split('.');
                    if (segments.Length >= 5)
                    {
                        // Parse game type from segment[4]
                        var gameType = segments[4] switch
                        {
                            "generals" => "Generals",
                            "zerohour" => "Zero Hour",
                            _ => segments[4].ToUpperInvariant()
                        };

                        // Parse content type from segment[3]
                        var contentTypeSegment = segments[3];
                        ContentType = contentTypeSegment switch
                        {
                            "gameinstallation" => "Game Installation",
                            "gameclient" => "Game Client",
                            _ => contentTypeSegment
                        };
                    }
                }
                catch
                {
                    // Ignore parsing errors
                }
            }
        }
    }
}