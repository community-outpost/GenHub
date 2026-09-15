---
title: Notification System
description: Toast notification system for user feedback and status updates
---

# Notification System

The notification system provides a modern toast-based UI for displaying user feedback, status updates, and important messages throughout the application.

---

## Overview

The notification system is built on a reactive architecture using `System.Reactive` and integrates seamlessly with Avalonia's UI thread dispatcher. It supports multiple notification types with automatic dismissal, manual dismissal, and optional action buttons.

### Key Features

- **Four Notification Types**: Info, Success, Warning, and Error
- **Auto-Dismiss**: Configurable timeout (default: 5 seconds)
- **Manual Dismiss**: Click-to-dismiss with X button
- **Thread-Safe**: Safe to call from any thread
- **Reactive**: Built on `IObservable<NotificationMessage>`
- **No Limit**: Unlimited notifications can stack
- **Smooth Animations**: Fade-in/fade-out transitions

---

## Architecture

### Components

```
┌─────────────────────────────────────────────────────────┐
│                   INotificationService                   │
│  (Singleton - Injectable into any ViewModel)            │
└────────────────────┬────────────────────────────────────┘
                     │
                     │ IObservable<NotificationMessage>
                     ▼
┌─────────────────────────────────────────────────────────┐
│            NotificationManagerViewModel                  │
│  (Manages collection of active notifications)           │
└────────────────────┬────────────────────────────────────┘
                     │
                     │ ObservableCollection
                     ▼
┌─────────────────────────────────────────────────────────┐
│            NotificationItemViewModel                     │
│  (Individual toast with auto-dismiss timer)             │
└─────────────────────────────────────────────────────────┘

                     │ IObservable<NotificationMessage>
                     │ (NotificationHistory)
                     ▼
┌─────────────────────────────────────────────────────────┐
│            NotificationFeedViewModel                    │
│  (Manages persistent notification history)              │
└────────────────────┬────────────────────────────────────┘
                     │
                     │ ObservableCollection
                     ▼
┌─────────────────────────────────────────────────────────┐
│            NotificationFeedItemViewModel                 │
│  (Individual feed item with actions)                   │
└─────────────────────────────────────────────────────────┘
```

### Models

- **`NotificationType`**: Enum (Info, Success, Warning, Error)
- **`NotificationSeverity`**: Priority levels for future filtering
- **`NotificationMessage`**: Data model containing title, message, type, and options
- **`NotificationAction`**: Represents an action button with text, callback, and style
- **`NotificationActionStyle`**: Enum for action button styles (Primary, Secondary, Danger, Success)

### Services

- **`INotificationService`**: Interface for showing notifications
- **`NotificationService`**: Implementation using `Subject<NotificationMessage>` with history tracking
- **`GitHubRateLimitTracker`**: Tracks GitHub API rate limits and provides warnings

### ViewModels

- **`NotificationManagerViewModel`**: Manages active notification collection
- **`NotificationItemViewModel`**: Represents individual toast with dismiss logic
- **`NotificationFeedViewModel`**: Manages persistent notification history
- **`NotificationFeedItemViewModel`**: Represents individual feed item with time formatting
- **`NotificationActionViewModel`**: Represents action button with styled brushes

### Views

- **`NotificationContainerView`**: Overlay container in top-right corner
- **`NotificationToastView`**: Individual toast UI with animations
- **`NotificationFeedView`**: Bell icon button with dropdown/flyout panel
- **`NotificationFeedItemView`**: Individual feed item UI with action buttons

---

## Usage

### Basic Usage

Inject `INotificationService` into your ViewModel:

```csharp
public class MyViewModel : ViewModelBase
{
    private readonly INotificationService _notificationService;

    public MyViewModel(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [RelayCommand]
    private void SaveData()
    {
        try
        {
            // Save logic...
            _notificationService.ShowSuccess(
                "Saved Successfully", 
                "Your changes have been saved.");
        }
        catch (Exception ex)
        {
            _notificationService.ShowError(
                "Save Failed", 
                $"Could not save: {ex.Message}");
        }
    }
}
```

### Notification Types

```csharp
// Info (Blue) - General information
_notificationService.ShowInfo("Update Available", "A new version is ready to install.");

// Success (Green) - Successful operations
_notificationService.ShowSuccess("Download Complete", "File downloaded successfully.");

// Warning (Orange) - Warnings and cautions
_notificationService.ShowWarning("Low Disk Space", "You have less than 1GB remaining.");

// Error (Red) - Errors and failures
_notificationService.ShowError("Connection Failed", "Could not connect to server.");
```

### Custom Auto-Dismiss Timeout

```csharp
// Show for 10 seconds instead of default 5
_notificationService.ShowSuccess(
    "Processing Complete", 
    "Your request has been processed.",
    autoDismissMs: 10000);

// Disable auto-dismiss (user must manually close)
_notificationService.ShowWarning(
    "Action Required", 
    "Please review the changes before continuing.",
    autoDismissMs: null);
```

### Advanced Usage with Actions

#### Single Action (Legacy)

```csharp
var notification = new NotificationMessage(
    NotificationType.Info,
    "Update Available",
    "Version 2.0 is ready to install.",
    autoDismissMs: null,
    actionText: "Install Now",
    action: () => InstallUpdate());

_notificationService.Show(notification);
```

#### Multiple Actions (New)

```csharp
var notification = new NotificationMessage(
    NotificationType.Info,
    "Profile Update Available",
    "A new version of your profile is available.",
    autoDismissMs: null,
    actions: new List<NotificationAction>
    {
        new NotificationAction(
            "Update Now",
            () => UpdateProfile(),
            NotificationActionStyle.Primary,
            dismissOnExecute: true),
        new NotificationAction(
            "Later",
            () => { /* Do nothing */ },
            NotificationActionStyle.Secondary,
            dismissOnExecute: true)
    });

_notificationService.Show(notification);
```

#### Confirm/Deny Pattern

```csharp
var notification = new NotificationMessage(
    NotificationType.Warning,
    "Delete Profile",
    "Are you sure you want to delete this profile?",
    autoDismissMs: null,
    actions: new List<NotificationAction>
    {
        new NotificationAction(
            "Confirm",
            () => DeleteProfile(),
            NotificationActionStyle.Danger,
            dismissOnExecute: true),
        new NotificationAction(
            "Cancel",
            () => { /* Do nothing */ },
            NotificationActionStyle.Secondary,
            dismissOnExecute: true)
    });

_notificationService.Show(notification);
```

---

## Integration

### Dependency Injection

The notification system is registered in `NotificationModule.cs`:

```csharp
services.AddSingleton<INotificationService, NotificationService>();
services.AddSingleton<NotificationManagerViewModel>();
services.AddSingleton<NotificationFeedViewModel>();
services.AddSingleton<GitHubRateLimitTracker>();
```

This is automatically included in `AppServices.ConfigureApplicationServices()`.

### MainWindow Integration

`NotificationContainerView` is added to `MainWindow.axaml`:

```xml
<Grid>
    <local:MainView x:Name="MainViewRoot" />
    <notifications:NotificationContainerView DataContext="{Binding NotificationManager}" />
</Grid>
```

### MainView Integration

`NotificationFeedView` is added to `MainView.axaml` in the header:

```xml
<Grid ColumnDefinitions="Auto,*,Auto" Margin="20,6,20,6" Height="75">
    <!-- Tab navigation - Left side -->
    <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Bottom" Margin="0,0,0,6">
        <!-- Tab buttons... -->
    </StackPanel>
    
    <!-- branding and notification feed - Right side -->
    <Grid Grid.Column="2" ColumnDefinitions="Auto,Auto,Auto" VerticalAlignment="Center">
        <!-- Notification feed bell icon -->
        <notifications:NotificationFeedView Grid.Column="0" DataContext="{Binding NotificationFeed}" Margin="0,0,8,0" />
        
        <!-- Logo and title section -->
        <StackPanel Grid.Column="2" HorizontalAlignment="Center" VerticalAlignment="Top" Margin="0,0,0,0">
            <Image Source="avares://GenHub/Assets/Logos/generalshub-logo.png" Width="90" Height="90" />
            <TextBlock Text="C&C Generals Launcher" FontSize="9" Foreground="#A0A0A0" />
        </StackPanel>
    </Grid>
</Grid>
```

The `NotificationManager` and `NotificationFeed` properties are injected into `MainViewModel`:

```csharp
public MainViewModel(
    // ... other parameters
    NotificationManagerViewModel notificationManager,
    NotificationFeedViewModel notificationFeedViewModel,
    // ... other parameters)
{
    NotificationManager = notificationManager;
    _notificationFeedViewModel = notificationFeedViewModel;
    // ...
}

public NotificationFeedViewModel NotificationFeed => _notificationFeedViewModel;
```

---

## API Reference

### INotificationService

```csharp
public interface INotificationService
{
    IObservable<NotificationMessage> Notifications { get; }
    IObservable<NotificationMessage> NotificationHistory { get; }
    
    void ShowInfo(string title, string message, int? autoDismissMs = null);
    void ShowSuccess(string title, string message, int? autoDismissMs = null);
    void ShowWarning(string title, string message, int? autoDismissMs = null);
    void ShowError(string title, string message, int? autoDismissMs = null);
    void Show(NotificationMessage notification);
    
    void Dismiss(Guid notificationId);
    void DismissAll();
    void MarkAsRead(Guid notificationId);
    void ClearHistory();
}
```

### NotificationMessage

```csharp
public record NotificationMessage(
    NotificationType Type,
    string Title,
    string Message,
    int? AutoDismissMilliseconds = 5000,
    string? ActionText = null,
    Action? Action = null,
    IReadOnlyList<NotificationAction>? Actions = null,
    bool IsPersistent = false,
    NotificationSeverity Severity = NotificationSeverity.Normal)
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public bool IsActionable => !string.IsNullOrEmpty(ActionText) && Action != null;
    public bool HasMultipleActions => Actions != null && Actions.Count > 0;
}
```

### NotificationAction

```csharp
public record NotificationAction(
    string Text,
    Action Callback,
    NotificationActionStyle Style = NotificationActionStyle.Primary,
    bool DismissOnExecute = true)
{
    // Properties are automatically generated from constructor parameters
}
```

### NotificationActionStyle

```csharp
public enum NotificationActionStyle
{
    Primary,    // Blue background, white text
    Secondary,  // Gray background, white text
    Danger,     // Red background, white text
    Success      // Green background, white text
}
```

### NotificationFeedViewModel

```csharp
public partial class NotificationFeedViewModel : ObservableObject, IDisposable
{
    public ObservableCollection<NotificationFeedItemViewModel> NotificationHistory { get; }
    public int UnreadCount { get; }
    public bool HasNotifications { get; }
    public bool IsFeedOpen { get; set; }
    
    public ICommand ToggleFeedCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand DismissNotificationCommand { get; }
    public ICommand MarkAsReadCommand { get; }
}
```

### GitHubRateLimitTracker

```csharp
public class GitHubRateLimitTracker
{
    public int RemainingRequests { get; }
    public int TotalRequests { get; }
    public DateTime ResetTime { get; }
    public TimeSpan TimeUntilReset { get; }
    public bool IsNearLimit { get; }
    public bool IsAtLimit { get; }
    public double RemainingPercentage { get; }
    
    public void UpdateFromHeaders(IDictionary<string, IEnumerable<string>> headers);
    public void UpdateFromException(GitHubOperationException exception);
    public string GetStatusMessage();
}
```

---

## Thread Safety

The notification system is **thread-safe** and can be called from any thread:

```csharp
// Safe to call from background thread
await Task.Run(() =>
{
    // Long-running operation...
    _notificationService.ShowSuccess("Background Task Complete", "Processing finished.");
});
```

All UI updates are automatically marshaled to the UI thread using `Dispatcher.UIThread.InvokeAsync` with `DispatcherPriority.Send` to ensure immediate, ordered processing.

---

## Performance Considerations

### Race Condition Prevention

The system uses a combination of:

- **Lock-based synchronization** (`lock (_lock)`) for collection access
- **High-priority dispatcher** (`DispatcherPriority.Send`) for immediate processing
- **InvokeAsync** instead of `Post` to prevent queue buildup

This ensures notifications can be triggered rapidly without freezing the UI.

### Memory Management

- Notifications are automatically disposed when dismissed
- Timers are properly cleaned up in `Dispose()`
- No memory leaks from rapid notification creation

---

## Testing

A test button is available in the Downloads tab for developers:

```csharp
[RelayCommand]
private void ShowDownloadCompleteNotification()
{
    _notificationService?.ShowSuccess(
        "Download Complete", 
        "The download has finished successfully.");
}
```

This allows testing the notification system without implementing actual features.

---

## Styling

Notification colors are defined in `NotificationItemViewModel`:

```csharp
public IBrush BackgroundBrush => Type switch
{
    NotificationType.Info => new SolidColorBrush(Color.Parse("#3498db")),    // Blue
    NotificationType.Success => new SolidColorBrush(Color.Parse("#27ae60")), // Green
    NotificationType.Warning => new SolidColorBrush(Color.Parse("#f39c12")), // Orange
    NotificationType.Error => new SolidColorBrush(Color.Parse("#e74c3c")),   // Red
    _ => new SolidColorBrush(Colors.Gray)
};
```

Animations are defined in `NotificationToastView.axaml`:

- **Fade-in**: 300ms opacity transition
- **Fade-out**: 200ms opacity transition

---

## GitHub Rate Limit Notifications

The `GitHubRateLimitTracker` automatically monitors GitHub API usage and provides warnings when approaching rate limits. When the remaining requests drop below 10% of the total limit, a warning notification is displayed.

### Rate Limit Warning Example

```csharp
// Automatically triggered by GitHubRateLimitTracker
_notificationService.ShowWarning(
    "GitHub API Rate Limit Warning",
    $"You have used {tracker.RemainingPercentage:P0} of your GitHub API quota. " +
    $"Resets in {tracker.FormatTimeSpan(tracker.TimeUntilReset)}.");
```

### Rate Limit Reached Example

```csharp
// Automatically triggered when limit is reached
_notificationService.ShowError(
    "GitHub API Rate Limit Reached",
    $"You have reached your GitHub API rate limit. " +
    $"Resets in {tracker.FormatTimeSpan(tracker.TimeUntilReset)}.");
```

---

## Notification Feed Features

The notification feed provides a persistent history of all notifications, accessible via the bell icon in the title bar.

### Feed Features

- **Persistent History**: Stores up to 100 notifications in memory
- **Read/Unread Tracking**: Visual indication of unread notifications
- **Actionable Notifications**: Perform actions directly from the feed
- **Clear All**: Remove all notifications from history
- **Individual Dismiss**: Remove specific notifications from history
- **Time Formatting**: Relative time display (e.g., "2 minutes ago", "1 hour ago")

### Feed Usage

The notification feed is automatically populated when notifications are shown. Users can:

1. Click the bell icon in the title bar to open the feed
2. View all past notifications with timestamps
3. Perform actions directly from actionable notifications
4. Mark notifications as read by viewing them
5. Clear all notifications using the "Clear All" button

---

## Future Enhancements

Potential improvements for future versions:

- **Notification Queue**: Limit visible notifications and queue overflow
- **Sound Effects**: Audio feedback for different notification types
- **Notification Groups**: Group related notifications
- **Persistent Notifications**: Save important notifications across sessions
- **Custom Templates**: Allow custom notification layouts
- **Notification Filtering**: Filter notifications by type or severity

---

## Storage & Workspace Notifications

The following notifications help users understand storage configuration and workspace strategy behavior:

### Storage Location Selection

When game installations are detected during a scan:

| Scenario | Notification Type | Message |
|----------|------------------|---------|
| Single drive | Info | "Storage Location Set" - CAS pool and workspaces will be created at the detected drive |
| Multiple drives | Warning | "Multiple Game Installations Found" - Lists drives and explains which one was auto-selected |
| Configuration success | Success | "Dynamic Storage Enabled" - Shows the configured CAS pool path |
| Configuration failure | Error | "Storage Setup Failed" - Explains why configuration failed |

### Workspace Strategy Fallback

When launching a profile with symlink strategies without admin rights:

| Scenario | Behavior |
|----------|----------|
| Same volume (hardlink possible) | Launch proceeds with hardlink fallback (logged) |
| Cross-drive (hardlink impossible) | Launch blocked with error explaining options: move CAS/workspace to same drive OR run as admin |

### User Settings Integration

- `UseInstallationAdjacentStorage`: When enabled, CAS pool and workspace paths are resolved dynamically from the preferred game installation
- `PreferredStorageInstallationId`: Persists the user's selected installation for storage

---

## Related

- [Architecture Overview](../architecture.md)
- [Dependency Injection](../dev/index.md#dependency-injection)
