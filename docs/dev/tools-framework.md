# Tools Framework Documentation

## Overview

The Tools framework provides a flexible, extensible architecture for adding utility tools to the GenHub application. Each tool is a self-contained component with its own view and view model that can interact with the main application's data and services.

## Architecture

### Core Components

1. **ITool** - Interface that all tools must implement
2. **IToolViewModel** - Interface for tool view models
3. **ToolMetadata** - Metadata describing a tool (name, description, category, etc.)
4. **IToolRegistry** - Registry for managing and discovering tools
5. **ToolViewModelBase** - Abstract base class providing common functionality

### Directory Structure

```
GenHub/Features/Tools/
├── Models/
│   └── GameInstallationInfoTool.cs         # Tool implementation
├── Services/
│   └── ToolRegistry.cs                     # Registry service
├── ViewModels/
│   ├── ToolsViewModel.cs                   # Main tools tab view model
│   ├── ToolViewModelBase.cs                # Base class for tool VMs
│   └── Tools/
│       └── GameInstallationInfoToolViewModel.cs
└── Views/
    ├── ToolsView.axaml                     # Main tools tab view
    └── Tools/
        ├── GameInstallationInfoToolView.axaml
        └── GameInstallationInfoToolView.axaml.cs
```

## Creating a New Tool

### Step 1: Create the Tool Implementation

Create a class that implements `ITool` in `GenHub/Features/Tools/Models/`:

```csharp
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Tools;
using GenHub.Features.Tools.ViewModels.Tools;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace GenHub.Features.Tools.Models;

public sealed class MyCustomTool : ITool
{
    private readonly IServiceProvider _serviceProvider;

    public MyCustomTool(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        Metadata = new ToolMetadata(
            id: "my-custom-tool",
            name: "My Custom Tool",
            description: "Description of what this tool does",
            category: "Utilities",
            order: 10);
    }

    public ToolMetadata Metadata { get; }

    public bool IsEnabled => true; // Or add logic to conditionally enable

    public IToolViewModel CreateViewModel()
    {
        return _serviceProvider.GetRequiredService<MyCustomToolViewModel>();
    }
}
```

### Step 2: Create the Tool ViewModel

Create a view model that inherits from `ToolViewModelBase` in `GenHub/Features/Tools/ViewModels/Tools/`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ViewModels.Tools;

public partial class MyCustomToolViewModel : ToolViewModelBase
{
    // Inject any required services
    private readonly ISomeService _someService;

    [ObservableProperty]
    private string _myProperty = "Initial value";

    public MyCustomToolViewModel(
        ISomeService someService,
        ILogger<MyCustomToolViewModel>? logger = null)
        : base(logger)
    {
        _someService = someService;
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        // Initialize your tool
        await LoadDataAsync();
    }

    public override async Task RefreshAsync()
    {
        await base.RefreshAsync();
        // Refresh logic
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        await ExecuteWithBusyStateAsync(async () =>
        {
            // Your logic here
            LogInformation("Loading data...");
        });
    }
}
```

### Step 3: Create the Tool View

Create the XAML view in `GenHub/Features/Tools/Views/Tools/`:

**MyCustomToolView.axaml:**
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:vm="clr-namespace:GenHub.Features.Tools.ViewModels.Tools"
             mc:Ignorable="d" d:DesignWidth="600" d:DesignHeight="500"
             x:Class="GenHub.Features.Tools.Views.Tools.MyCustomToolView"
             x:DataType="vm:MyCustomToolViewModel">

    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Spacing="16" Margin="0">
            <!-- Your UI here -->
            <Border Background="#2A2A2A" CornerRadius="8" Padding="20">
                <TextBlock Text="{Binding MyProperty}" 
                           FontSize="14" 
                           Foreground="White" />
            </Border>
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

**MyCustomToolView.axaml.cs:**
```csharp
using Avalonia.Controls;

namespace GenHub.Features.Tools.Views.Tools;

public partial class MyCustomToolView : UserControl
{
    public MyCustomToolView()
    {
        InitializeComponent();
    }
}
```

### Step 4: Register in Dependency Injection

Update `GenHub/Infrastructure/DependencyInjection/SharedViewModelModule.cs`:

```csharp
// Register tool implementations
services.AddSingleton<ITool, GameInstallationInfoTool>();
services.AddSingleton<ITool, MyCustomTool>(); // Add this line

// Register tool view models
services.AddTransient<GameInstallationInfoToolViewModel>();
services.AddTransient<MyCustomToolViewModel>(); // Add this line
```

### Step 5: Add DataTemplate Mapping

Update `GenHub/Common/Views/MainView.axaml`:

1. Add namespace:
```xml
xmlns:myToolVM="clr-namespace:GenHub.Features.Tools.ViewModels.Tools"
xmlns:myToolViews="clr-namespace:GenHub.Features.Tools.Views.Tools"
```

2. Add DataTemplate:
```xml
<DataTemplate DataType="myToolVM:MyCustomToolViewModel">
    <myToolViews:MyCustomToolView />
</DataTemplate>
```

## Features Available to Tools

### Base Functionality (from ToolViewModelBase)

- **IsBusy** - Automatic busy state management
- **IsActive** - Track if tool is currently active
- **ExecuteWithBusyStateAsync()** - Execute operations with automatic busy state
- **LogError()** / **LogInformation()** - Built-in logging
- **InitializeAsync()** - Called once when tool is created
- **ActivateAsync()** - Called when user selects the tool
- **DeactivateAsync()** - Called when user navigates away
- **RefreshAsync()** - Called when user clicks refresh

### Accessing Application Services

Tools can access any registered service through dependency injection:

- **IGameInstallationDetectionOrchestrator** - Detect game installations
- **IGameProfileService** - Manage game profiles
- **IConfigurationProviderService** - Access configuration
- **IUserSettingsService** - Access user settings
- Any custom services registered in DI

### Example: Accessing Game Data

```csharp
public partial class MyToolViewModel : ToolViewModelBase
{
    private readonly IGameProfileService _profileService;
    private readonly IGameInstallationDetectionOrchestrator _installOrchestrator;

    public MyToolViewModel(
        IGameProfileService profileService,
        IGameInstallationDetectionOrchestrator installOrchestrator,
        ILogger<MyToolViewModel>? logger = null)
        : base(logger)
    {
        _profileService = profileService;
        _installOrchestrator = installOrchestrator;
    }

    private async Task LoadGameDataAsync()
    {
        var profiles = await _profileService.GetAllProfilesAsync();
        var installations = await _installOrchestrator.DetectAllInstallationsAsync();
        
        // Process data...
    }
}
```

## Tool Categories

Tools are organized by category in the UI. Standard categories include:

- **Diagnostics** - System and game diagnostics
- **Utilities** - General utilities
- **Maintenance** - Maintenance and cleanup tools
- **General** - Default category

## Best Practices

1. **Keep tools focused** - Each tool should do one thing well
2. **Use ExecuteWithBusyStateAsync** - Always wrap long operations
3. **Implement RefreshAsync** - Allow users to refresh data
4. **Log important actions** - Use the built-in logging methods
5. **Handle errors gracefully** - Show user-friendly error messages
6. **Follow naming conventions** - Use consistent naming for files and classes
7. **Test with DI** - Ensure all dependencies are properly registered

## Example Tool Implementation

See `GameInstallationInfoTool` for a complete working example that demonstrates:
- Service injection
- Data binding
- Command implementation
- Error handling
- UI layout
- Refresh functionality

## Troubleshooting

### Tool doesn't appear in the list
- Check that the tool is registered in `SharedViewModelModule.cs`
- Verify `IsEnabled` returns `true`
- Check the registry initialization code runs

### Tool view doesn't display
- Ensure DataTemplate is registered in `MainView.axaml`
- Verify namespace imports are correct
- Check that `CreateViewModel()` returns the correct type

### Can't access services
- Verify services are registered in DI container
- Check constructor parameters match registered types
- Ensure tool view model is registered as Transient