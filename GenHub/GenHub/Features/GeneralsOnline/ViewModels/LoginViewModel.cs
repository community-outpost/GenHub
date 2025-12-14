using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GeneralsOnline;
using GenHub.Core.Models.GeneralsOnline;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GeneralsOnline.ViewModels;

/// <summary>
/// ViewModel for the GeneralsOnline login screen.
/// Handles gamecode-based polling authentication flow.
/// </summary>
public partial class LoginViewModel : ViewModelBase
{
    private readonly IGeneralsOnlineApiClient _apiClient;
    private readonly IGeneralsOnlineAuthService _authService;
    private readonly IExternalLinkService _externalLinkService;
    private readonly ILogger<LoginViewModel> _logger;

    private CancellationTokenSource? _pollingCancellation;

    [ObservableProperty]
    private bool _isPolling;

    [ObservableProperty]
    private string _gameCode = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Ready to log in";

    [ObservableProperty]
    private bool _isLoggedIn;

    [ObservableProperty]
    private string _displayName = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoginViewModel"/> class.
    /// </summary>
    /// <param name="apiClient">The API client.</param>
    /// <param name="authService">The authentication service.</param>
    /// <param name="externalLinkService">The external link service.</param>
    /// <param name="logger">The logger instance.</param>
    public LoginViewModel(
        IGeneralsOnlineApiClient apiClient,
        IGeneralsOnlineAuthService authService,
        IExternalLinkService externalLinkService,
        ILogger<LoginViewModel> logger)
    {
        _apiClient = apiClient;
        _authService = authService;
        _externalLinkService = externalLinkService;
        _logger = logger;

        // Subscribe to auth state changes - dispatch to UI thread to avoid threading issues
        _authService.IsAuthenticated.Subscribe(isAuthenticated =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsLoggedIn = isAuthenticated;
                if (isAuthenticated)
                {
                    DisplayName = _authService.CurrentDisplayName ?? "Unknown User";
                    StatusMessage = $"Logged in as {DisplayName}";
                }
                else
                {
                    DisplayName = string.Empty;
                    StatusMessage = "Ready to log in";
                }
            });
        });
    }

    /// <summary>
    /// Command to initiate the login flow with gamecode polling.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        _logger.LogInformation("LoginAsync command invoked");
        
        try
        {
            // Generate 32-character alphanumeric gamecode as per GO API spec
            GameCode = _authService.GenerateGameCode();
            _logger.LogInformation("Generated gamecode: {GameCode}", GameCode);

            // Open browser with gamecode
            var loginUrl = _authService.GetLoginUrl(GameCode);
            StatusMessage = "Opening browser for login...";

            var success = _externalLinkService.OpenUrl(loginUrl);
            if (!success)
            {
                _logger.LogWarning("Failed to open login URL");
                StatusMessage = "Failed to open browser. Please try again.";
                GameCode = string.Empty;
                return;
            }

            // Start polling
            IsPolling = true;
            StatusMessage = "Waiting for authentication...";
            _logger.LogInformation("Starting polling for gamecode: {GameCode}", GameCode);

            await PollForLoginAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login process");
            StatusMessage = "An error occurred. Please try again.";
            IsPolling = false;
            GameCode = string.Empty;
        }
    }

    /// <summary>
    /// Cancels the current polling operation.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCancelLogin))]
    private void CancelLogin()
    {
        _pollingCancellation?.Cancel();
        IsPolling = false;
        GameCode = string.Empty;
        StatusMessage = "Login cancelled";
        _logger.LogInformation("Login cancelled by user");
    }

    /// <summary>
    /// Command to log out the current user.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLogout))]
    private async Task LogoutAsync()
    {
        try
        {
            _logger.LogInformation("User initiated logout");
            await _authService.LogoutAsync();
            StatusMessage = "Logged out successfully";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            StatusMessage = "Error during logout. Please try again.";
        }
    }

    /// <summary>
    /// Command to open the registration page.
    /// </summary>
    [RelayCommand]
    private void OpenRegistrationPage()
    {
        try
        {
            if (!_externalLinkService.OpenUrl("https://www.playgenerals.online/register"))
            {
                _logger.LogWarning("Failed to open registration URL");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening registration page");
        }
    }

    private bool CanLogin() => !IsPolling && !IsLoggedIn;

    private bool CanCancelLogin() => IsPolling;

    private bool CanLogout() => IsLoggedIn && !IsPolling;

    private async Task PollForLoginAsync()
    {
        _pollingCancellation?.Cancel();
        _pollingCancellation = new CancellationTokenSource();

        try
        {
            // Poll for 5 minutes (300 attempts at 1 second intervals, as per example code)
            var maxAttempts = 300;
            var pollInterval = TimeSpan.FromSeconds(1);

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                // Check for cancellation
                _pollingCancellation.Token.ThrowIfCancellationRequested();

                _logger.LogDebug("Polling attempt {Attempt}/{MaxAttempts} for gamecode {GameCode}", attempt, maxAttempts, GameCode);

                // Call the CheckLogin endpoint
                var result = await _apiClient.CheckLoginAsync(GameCode, _pollingCancellation.Token);

                if (result != null)
                {
                    if (result.IsSuccess)
                    {
                        // Success! Process the login
                        _logger.LogInformation("Login successful for gamecode {GameCode}", GameCode);
                        StatusMessage = "Login successful!";

                        await _authService.ProcessLoginSuccessAsync(result);

                        IsPolling = false;
                        GameCode = string.Empty;
                        return;
                    }
                    else if (result.IsFailed)
                    {
                        // Login explicitly failed
                        _logger.LogWarning("Login failed for gamecode {GameCode}: {State}", GameCode, result.Result);
                        StatusMessage = "Login failed. Please try again.";
                        IsPolling = false;
                        GameCode = string.Empty;
                        return;
                    }

                    // If still pending/waiting, continue polling
                }

                // Wait before next poll (but not on last iteration)
                if (attempt < maxAttempts)
                {
                    await Task.Delay(pollInterval, _pollingCancellation.Token);
                }
            }

            // Timed out
            _logger.LogWarning("Login timed out for gamecode {GameCode}", GameCode);
            StatusMessage = "Login timed out. Please try again.";
            IsPolling = false;
            GameCode = string.Empty;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Polling cancelled");
            // Status already set by CancelLogin
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during polling");
            StatusMessage = "An error occurred. Please try again.";
            IsPolling = false;
            GameCode = string.Empty;
        }
        finally
        {
            _pollingCancellation?.Dispose();
            _pollingCancellation = null;
        }
    }

    partial void OnIsPollingChanged(bool value)
    {
        LoginCommand?.NotifyCanExecuteChanged();
        CancelLoginCommand?.NotifyCanExecuteChanged();
        LogoutCommand?.NotifyCanExecuteChanged();
    }

    partial void OnIsLoggedInChanged(bool value)
    {
        LoginCommand?.NotifyCanExecuteChanged();
        LogoutCommand?.NotifyCanExecuteChanged();
    }
}
