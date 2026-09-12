using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// Mock demo window for game profile settings.
/// </summary>
public partial class DemoGameProfileSettingsWindowMock : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DemoGameProfileSettingsWindowMock"/> class.
    /// </summary>
    public DemoGameProfileSettingsWindowMock()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
