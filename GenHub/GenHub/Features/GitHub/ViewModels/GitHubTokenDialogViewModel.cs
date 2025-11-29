using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GitHub;
using Microsoft.Extensions.Logging;
using Octokit;
using System;
using System.Diagnostics;
using System.Security;
using System.Threading.Tasks;

namespace GenHub.Features.GitHub.ViewModels;

/// <summary>
/// ViewModel for GitHub authentication token dialog.
/// </summary>
public partial class GitHubTokenDialogViewModel : ObservableObject
{
    private readonly IGitHubApiClient _gitHubApiClient;
    private readonly IGitHubTokenStorage _tokenStorage;
    private readonly ILogger<GitHubTokenDialogViewModel> _logger;
    private SecureString _secureToken = new SecureString();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isValidating;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessageColor))]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string _validationMessage = GitHubConstants.EnterTokenMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(ValidationMessageColor))]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private bool _isTokenValid;

    [ObservableProperty]
    private bool _shouldClose;

    [ObservableProperty]
    private bool _dialogResult;

    /// <summary>
    /// Sets the secure token from the UI.
    /// </summary>
    /// <param name="token">The secure token.</param>
    public void SetSecureToken(SecureString token)
    {
        _secureToken = token ?? new SecureString();
        OnPropertyChanged(nameof(CanSave));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubTokenDialogViewModel"/> class.
    /// </summary>
    /// <param name="gitHubApiClient">The GitHub API client.</param>
    /// <param name="tokenStorage">The token storage service.</param>
    /// <param name="logger">The logger instance.</param>
    public GitHubTokenDialogViewModel(
        IGitHubApiClient gitHubApiClient,
        IGitHubTokenStorage tokenStorage,
        ILogger<GitHubTokenDialogViewModel> logger)
    {
        _gitHubApiClient = gitHubApiClient ?? throw new ArgumentNullException(nameof(gitHubApiClient));
        _tokenStorage = tokenStorage ?? throw new ArgumentNullException(nameof(tokenStorage));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets a value indicating whether the Save command can execute.
    /// </summary>
    public bool CanSave => IsTokenValid && !IsValidating && _secureToken.Length > 0;

    /// <summary>
    /// Gets the validation message brush based on token validity.
    /// </summary>
    public SolidColorBrush ValidationMessageColor => IsTokenValid
        ? new SolidColorBrush(Color.Parse("#28A745")) // Green for success
        : new SolidColorBrush(Color.Parse("#DC3545")); // Red for error

    /// <summary>
    /// Gets a value indicating whether there is a validation message to show.
    /// </summary>
    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage) &&
        ValidationMessage != GitHubConstants.EnterTokenMessage &&
        ValidationMessage != GitHubConstants.ValidatingTokenMessage;

    /// <summary>
    /// Validates the entered token.
    /// </summary>
    /// <returns>A task representing the validation operation.</returns>
    [RelayCommand]
    public async Task ValidateTokenAsync()
    {
        if (_secureToken.Length == 0)
        {
            ValidationMessage = GitHubConstants.EnterTokenMessage;
            IsTokenValid = false;
            return;
        }

        try
        {
            IsValidating = true;
            ValidationMessage = GitHubConstants.ValidatingTokenMessage;
            IsTokenValid = false;

            // Set the token for validation
            _gitHubApiClient.SetAuthenticationToken(_secureToken);

            // Actually test the token by making a simple API call to get the authenticated user
            try
            {
                // Use GetAuthenticatedUser instead of trying to fetch a specific release
                var user = await _gitHubApiClient.GetAuthenticatedUserAsync();

                if (user != null)
                {
                    ValidationMessage = $"✅ Token validated successfully! Authenticated as: {user.Login}";
                    IsTokenValid = true;
                    _logger.LogInformation("GitHub token validated successfully for user: {Login}", user.Login);
                }
                else
                {
                    ValidationMessage = "⚠️ Token set but user verification failed. You can still try saving.";
                    IsTokenValid = true;
                    _logger.LogWarning("GitHub token validation returned null user");
                }
            }
            catch (RateLimitExceededException rateLimitEx)
            {
                // Rate limit means the token is valid but we've hit limits
                ValidationMessage = "⚠️ Rate limited! Token is valid but GitHub API limit reached. You can still save.";
                IsTokenValid = true;
                _logger.LogInformation("GitHub token validated (rate limited): {Message}", rateLimitEx.Message);
            }
            catch (ForbiddenException forbiddenEx)
            {
                // Organization-restricted token or insufficient permissions
                ValidationMessage = "⚠️ Token validated but has restrictions (e.g., organization policy, limited lifetime). You can save but some features may not work.";
                IsTokenValid = true;
                _logger.LogWarning(forbiddenEx, "GitHub token has access restrictions: {Message}", forbiddenEx.Message);
            }
            catch (AuthorizationException authEx)
            {
                ValidationMessage = "❌ Invalid token or insufficient permissions. Please check your token.";
                IsTokenValid = false;
                _logger.LogWarning(authEx, "GitHub token authorization failed");
            }
            catch (Exception apiEx)
            {
                // Check if it's a rate limit disguised as another error
                if (apiEx.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
                    apiEx.Message.Contains("API rate limit", StringComparison.OrdinalIgnoreCase))
                {
                    ValidationMessage = "⚠️ Rate limited! Token appears valid but hit GitHub rate limits. You can save.";
                    IsTokenValid = true;
                    _logger.LogInformation("Rate limit detected in API error: {Message}", apiEx.Message);
                }
                else
                {
                    ValidationMessage = $"⚠️ Token set but API test failed: {apiEx.Message.Substring(0, Math.Min(100, apiEx.Message.Length))}";
                    IsTokenValid = true; // Allow saving since token format might be correct
                    _logger.LogWarning(apiEx, "GitHub API test failed but token may be valid");
                }
            }

            // Manually notify command that CanExecute state may have changed
            SaveCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token validation failed");
            ValidationMessage = string.Format(GitHubConstants.InvalidTokenFormat, ex.Message);
            IsTokenValid = false;
            SaveCommand.NotifyCanExecuteChanged();
        }
        finally
        {
            IsValidating = false;
            SaveCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Saves the token and closes the dialog.
    /// </summary>
    /// <returns>A task representing the save operation.</returns>
    [RelayCommand(CanExecute = nameof(CanSave))]
    public async Task SaveAsync()
    {
        _logger.LogInformation(
            "Save button clicked. CanSave: {CanSave}, IsTokenValid: {IsTokenValid}, IsValidating: {IsValidating}, Token length: {TokenLength}",
            CanSave,
            IsTokenValid,
            IsValidating,
            _secureToken.Length);
        if (CanSave && IsTokenValid)
        {
            try
            {
                // Token is already set in the API client from validation
                // Now persist it to secure storage
                await _tokenStorage.SaveTokenAsync(_secureToken);

                DialogResult = true;
                ShouldClose = true;
                _logger.LogInformation("Token saved successfully and persisted to secure storage");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist token to secure storage");
                ValidationMessage = "⚠️ Token set but failed to save to secure storage. It will be lost on restart.";

                // Still close with success since token is set in memory
                DialogResult = true;
                ShouldClose = true;
            }
        }
        else
        {
            _logger.LogWarning("Save attempted but conditions not met. CanSave: {CanSave}, IsTokenValid: {IsTokenValid}", CanSave, IsTokenValid);
        }
    }

    /// <summary>
    /// Cancels the dialog without saving.
    /// </summary>
    [RelayCommand]
    public void Cancel()
    {
        DialogResult = false;
        ShouldClose = true;
    }

    /// <summary>
    /// Resets the dialog state.
    /// </summary>
    public void Reset()
    {
        _secureToken = new SecureString();
        IsTokenValid = false;
        ValidationMessage = GitHubConstants.EnterTokenMessage;
        ShouldClose = false;
        DialogResult = false;
        SaveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Opens the GitHub Personal Access Token creation page.
    /// </summary>
    [RelayCommand]
    public void OpenPatCreation()
    {
        try
        {
            var url = "https://github.com/settings/tokens/new?scopes=repo,workflow&description=GenHub%20Access";
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
            _logger.LogInformation("Opened GitHub PAT creation URL: {Url}", url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open GitHub PAT creation URL");
            ValidationMessage = "Could not open browser. Please visit: https://github.com/settings/tokens/new";
        }
    }
}
