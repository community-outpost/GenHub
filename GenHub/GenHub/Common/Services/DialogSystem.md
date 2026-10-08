# Dialog System

GenHub utilizes a service-based dialog system to display modal windows while adhering to MVVM principles.

## Core Components

### 1. IDialogService
The primary interface for interacting with dialogs. Inject this into your ViewModels.

**Methods:**
- `ShowConfirmationAsync`: Displays a standard Yes/No confirmation dialog.
- `ShowMessageAsync`: Displays a generic, customizable message dialog (GenericMessageWindow).

### 2. genericMessageWindow
A reusable, aesthetic dialog window designed for:
- Welcome/First-run experiences
- Changelogs
- Announcements
- Warnings with custom actions

**Features:**
- **Glassmorphism:** Uses AcrylicBlur transparency and gradient borders.
- **Markdown Support:** Content is rendered using `Markdown.Avalonia`, enabling rich text, lists, and links.
- **Custom Actions:** Supports any number of buttons (`DialogActionViewModel`) with distinct styles (Primary, Success, Secondary).
- **"Don't Ask Again":** Built-in logic to return a generic "Do Not Ask Again" boolean state, which can be persisted by the caller.

## Usage Example

```csharp
// 1. Define Actions
var actions = new[]
{
    new DialogActionViewModel
    {
        Text = "Learn More",
        Style = NotificationActionStyle.Primary,
        Action = () => { /* Navigate */ }
    },
    new DialogActionViewModel
    {
        Text = "Dismiss",
        Style = NotificationActionStyle.Secondary
    }
};

// 2. call Service
var result = await _dialogService.ShowMessageAsync(
    title: "New Feature",
    content: "**Bold text** and [Links](http://example.com)",
    actions: actions,
    showDoNotAskAgain: true
);

// 3. Handle Result
if (result.DoNotAskAgain)
{
    // Save preference
}
```

## Styling
The window uses predefined styles for buttons compatible with the `NotificationActionStyle` enum:
- `Primary` (Violet)
- `Success` (Emerald)
- `Secondary` (Slate)
