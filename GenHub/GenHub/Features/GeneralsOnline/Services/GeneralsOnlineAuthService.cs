using System;
using System.IO;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GeneralsOnline;
using GenHub.Core.Models.GeneralsOnline;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GeneralsOnline.Services;

/// <summary>
/// Service for managing Generals Online authentication state.
/// Monitors the credentials file and provides reactive updates.
/// Handles both browser-based login and stored credential login flows.
/// </summary>
/// <param name="credentialsStorage">The credentials storage service.</param>
/// <param name="apiClient">The API client for authentication calls.</param>
/// <param name="logger">The logger instance.</param>
public class GeneralsOnlineAuthService(
    ICredentialsStorageService credentialsStorage,
    IGeneralsOnlineApiClient apiClient,
    ILogger<GeneralsOnlineAuthService> logger) : IGeneralsOnlineAuthService, IDisposable
{
    private static readonly char[] GameCodeChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

    private readonly BehaviorSubject<bool> _isAuthenticatedSubject = new(false);
    private FileSystemWatcher? _fileWatcher;
    private IDisposable? _fileChangeSubscription;
    private CredentialsModel? _currentCredentials;
    private string? _currentSessionToken;
    private string? _currentDisplayName;
    private long? _currentUserId;
    private bool _disposed;

    /// <inheritdoc />
    public IObservable<bool> IsAuthenticated => _isAuthenticatedSubject.AsObservable();

    /// <inheritdoc />
    public string? CurrentToken => _currentCredentials?.RefreshToken;

    /// <inheritdoc />
    public string? CurrentSessionToken => _currentSessionToken;

    /// <inheritdoc />
    public string? CurrentDisplayName => _currentDisplayName;

    /// <inheritdoc />
    public long? CurrentUserId => _currentUserId;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        try
        {
            // First, check if we have stored credentials
            var hasCredentials = await ValidateCredentialsAsync().ConfigureAwait(false);

            if (hasCredentials && _currentCredentials != null)
            {
                // Attempt silent login with stored credentials
                logger.LogInformation("Found stored credentials, attempting silent login");
                var result = await TryLoginWithStoredCredentialsAsync().ConfigureAwait(false);

                if (result != null && result.IsSuccess)
                {
                    await ProcessLoginSuccessAsync(result).ConfigureAwait(false);
                    logger.LogInformation("Silent login successful for user: {DisplayName}", result.DisplayName);
                }
                else
                {
                    logger.LogWarning("Silent login failed, credentials may be expired");

                    // Don't clear credentials yet - user can try manual login
                }
            }

            SetupFileWatcher();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize GeneralsOnlineAuthService");
        }
    }

    /// <inheritdoc />
    public async Task<bool> ValidateCredentialsAsync()
    {
        try
        {
            var credentials = await credentialsStorage.LoadCredentialsAsync().ConfigureAwait(false);

            if (credentials == null)
            {
                logger.LogDebug("No valid credentials found");
                UpdateAuthState(null);
                return false;
            }

            if (!credentials.IsValid())
            {
                logger.LogWarning("Loaded credentials are invalid");
                UpdateAuthState(null);
                return false;
            }

            logger.LogInformation("Successfully validated credentials file");
            _currentCredentials = credentials;
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error validating credentials file");
            UpdateAuthState(null);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> VerifyTokenWithServerAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var result = await apiClient.LoginWithTokenAsync(token).ConfigureAwait(false);
            return result != null && result.IsSuccess;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error verifying token with server");
            return false;
        }
    }

    /// <inheritdoc />
    public Task<string?> GetAuthTokenAsync()
    {
        // Return the session token for authenticated API calls, fall back to refresh token
        return Task.FromResult(_currentSessionToken ?? _currentCredentials?.RefreshToken);
    }

    /// <inheritdoc />
    public async Task SaveRefreshTokenAsync(string refreshToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        try
        {
            var credentials = new CredentialsModel
            {
                RefreshToken = refreshToken,
            };

            await credentialsStorage.SaveCredentialsAsync(credentials).ConfigureAwait(false);
            logger.LogInformation("Successfully saved refresh token");

            _currentCredentials = credentials;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save refresh token");
            throw;
        }
    }

    /// <inheritdoc />
    public string GenerateGameCode()
    {
        var random = new Random();
        var gameCode = new StringBuilder(GeneralsOnlineConstants.GameCodeLength);

        for (int i = 0; i < GeneralsOnlineConstants.GameCodeLength; i++)
        {
            gameCode.Append(GameCodeChars[random.Next(GameCodeChars.Length)]);
        }

        return gameCode.ToString();
    }

    /// <inheritdoc />
    public string GetLoginUrl(string gameCode)
    {
        return $"{GeneralsOnlineConstants.LoginUrlBase}/?gamecode={gameCode}&client={GeneralsOnlineConstants.ClientId}";
    }

    /// <inheritdoc />
    public async Task<LoginResult?> TryLoginWithStoredCredentialsAsync(CancellationToken cancellationToken = default)
    {
        if (_currentCredentials == null || !_currentCredentials.IsValid())
        {
            var loaded = await credentialsStorage.LoadCredentialsAsync().ConfigureAwait(false);
            if (loaded == null || !loaded.IsValid())
            {
                logger.LogDebug("No stored credentials available for silent login");
                return null;
            }

            _currentCredentials = loaded;
        }

        try
        {
            logger.LogDebug("Attempting login with stored refresh token");
            var result = await apiClient.LoginWithTokenAsync(_currentCredentials.RefreshToken, cancellationToken).ConfigureAwait(false);

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during silent login attempt");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task ProcessLoginSuccessAsync(LoginResult loginResult)
    {
        ArgumentNullException.ThrowIfNull(loginResult);

        if (!loginResult.IsSuccess)
        {
            logger.LogWarning("ProcessLoginSuccess called with non-successful result: {State}", loginResult.Result);
            return;
        }

        try
        {
            // Save the new refresh token (it may have been refreshed)
            if (!string.IsNullOrEmpty(loginResult.RefreshToken))
            {
                await SaveRefreshTokenAsync(loginResult.RefreshToken).ConfigureAwait(false);
            }

            // Update session state
            _currentSessionToken = loginResult.SessionToken;
            _currentDisplayName = loginResult.DisplayName;
            _currentUserId = loginResult.UserId > 0 ? loginResult.UserId : null;

            // Update auth state to notify observers
            UpdateAuthState(_currentCredentials);

            logger.LogInformation(
                "Login successful - User: {DisplayName}, UserId: {UserId}",
                _currentDisplayName,
                _currentUserId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing login success");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task LogoutAsync()
    {
        try
        {
            logger.LogInformation("Logging out user: {DisplayName}", _currentDisplayName);

            // Clear credentials file
            await credentialsStorage.DeleteCredentialsAsync().ConfigureAwait(false);

            // Clear all state
            _currentCredentials = null;
            _currentSessionToken = null;
            _currentDisplayName = null;
            _currentUserId = null;

            // Notify observers
            UpdateAuthState(null);

            logger.LogInformation("Logout complete");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during logout");
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases unmanaged and - optionally - managed resources.
    /// </summary>
    /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _fileWatcher?.Dispose();
            _fileChangeSubscription?.Dispose();
            _isAuthenticatedSubject.Dispose();
        }

        _disposed = true;
    }

    private void SetupFileWatcher()
    {
        try
        {
            var path = credentialsStorage.GetCredentialsPath();
            var directory = Path.GetDirectoryName(path);
            var filename = Path.GetFileName(path);

            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(filename))
            {
                logger.LogError("Invalid credentials path: {Path}", path);
                return;
            }

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _fileWatcher = new FileSystemWatcher(directory, filename)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };

            // Create observable from file system events and throttle to avoid excessive validation calls
            var fileChangedObservable = Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(
                h => _fileWatcher.Changed += h,
                h => _fileWatcher.Changed -= h)
                .Select(e => e.EventArgs)
                .Merge(Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(
                    h => _fileWatcher.Created += h,
                    h => _fileWatcher.Created -= h)
                    .Select(e => e.EventArgs))
                .Merge(Observable.FromEventPattern<RenamedEventHandler, RenamedEventArgs>(
                    h => _fileWatcher.Renamed += h,
                    h => _fileWatcher.Renamed -= h)
                    .Select(e => e.EventArgs))
                .Merge(Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(
                    h => _fileWatcher.Deleted += h,
                    h => _fileWatcher.Deleted -= h)
                    .Select(e => e.EventArgs));

            // Throttle events to avoid excessive validation calls during file writes
            _fileChangeSubscription = fileChangedObservable
                .Throttle(TimeSpan.FromMilliseconds(500))
                .Subscribe(async _ => await ValidateCredentialsAsync());

            logger.LogInformation("Started monitoring credentials file at {Path}", path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to setup file watcher");
        }
    }

    private void UpdateAuthState(CredentialsModel? credentials)
    {
        var isAuthenticated = credentials != null && credentials.IsValid() && _currentSessionToken != null;

        if (_isAuthenticatedSubject.Value != isAuthenticated)
        {
            _isAuthenticatedSubject.OnNext(isAuthenticated);
            logger.LogInformation("Authentication state changed: {IsAuthenticated}", isAuthenticated);
        }
    }
}
