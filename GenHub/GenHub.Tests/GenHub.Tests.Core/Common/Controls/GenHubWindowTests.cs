using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GenHub.Common.Controls;
using GenHub.Common.Views;
using GenHub.Common.Views.Dialogs;
using GenHub.Features.AppUpdate.Views;
using GenHub.Features.Downloads.Views;
using GenHub.Features.GameProfiles.Views;
using GenHub.Features.GameProfiles.Views.Wizard;
using GenHub.Features.Tools.ModBuilder.Views;
using GenHub.Features.Tools.ReplayManager.Views;
using GenHub.Features.Tools.Views;
using GenHub.Features.Tools.Views.Dialogs;
using System;
using System.Collections.Generic;
using Xunit;

namespace GenHub.Tests.Core.Common.Controls;

/// <summary>
/// Unit tests for <see cref="GenHubWindow"/>.
/// </summary>
public class GenHubWindowTests
{
    /// <summary>
    /// Verifies that opening a <see cref="GenHubWindow"/> applies platform decorations,
    /// overriding any explicit XAML value the same way XAML assignment would.
    /// </summary>
    [AvaloniaFact]
    public void Opened_AppliesPlatformDecorations()
    {
        var window = new GenHubWindow { SystemDecorations = SystemDecorations.Full };
        window.Show();

        Assert.IsAssignableFrom<Window>(window);
        if (OperatingSystem.IsLinux())
        {
            Assert.Equal(SystemDecorations.None, window.SystemDecorations);
        }
        else
        {
            Assert.Equal(SystemDecorations.Full, window.SystemDecorations);
        }

        window.Close();
    }

    /// <summary>
    /// Verifies that every migrated window constructs and opens without throwing,
    /// which validates XAML event-handler resolution against the shared base class.
    /// </summary>
    [AvaloniaFact]
    public void MigratedWindows_ConstructAndOpen()
    {
        var failures = new List<string>();
        foreach (var (name, factory) in MigratedWindows())
        {
            try
            {
                var window = factory();
                window.Show();
                window.Close();
            }
            catch (Exception ex)
            {
                failures.Add($"{name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0, $"Migrated windows failed to construct or open:\n{string.Join("\n", failures)}");
    }

    /// <summary>
    /// Verifies that closing a window with <c>DisposeDataContextOnClose</c> disposes the data context.
    /// </summary>
    [AvaloniaFact]
    public void Closed_WithDisposeOptIn_DisposesDataContext()
    {
        var window = new DisposeOptInWindow();
        var disposable = new DisposableViewModel();
        window.DataContext = disposable;
        window.Show();

        window.Close();

        Assert.True(disposable.IsDisposed);
    }

    /// <summary>
    /// Verifies that <see cref="GameProfileSettingsWindow"/> opts out of fit-to-screen
    /// because it owns persisted placement logic that clamping would corrupt.
    /// </summary>
    [AvaloniaFact]
    public void GameProfileSettingsWindow_OptsOutOfFitToScreen()
    {
        var window = new GameProfileSettingsWindow();
        window.Show();

        Assert.Equal(double.PositiveInfinity, window.MaxWidth);
        Assert.Equal(double.PositiveInfinity, window.MaxHeight);

        window.Close();
    }

    /// <summary>
    /// Gets factories for every window migrated to <see cref="GenHubWindow"/>.
    /// </summary>
    /// <returns>The window names and factories.</returns>
    private static IEnumerable<(string Name, Func<GenHubWindow> Factory)> MigratedWindows()
    {
        yield return (nameof(MainWindow), () => new MainWindow());
        yield return (nameof(UpdateOptionDialogWindow), () => new UpdateOptionDialogWindow());
        yield return (nameof(GenericMessageWindow), () => new GenericMessageWindow());
        yield return (nameof(ConfirmationDialogWindow), () => new ConfirmationDialogWindow());
        yield return (nameof(ShareLinksDialog), () => new ShareLinksDialog());
        yield return (nameof(ConfigEditorDialog), () => new ConfigEditorDialog());
        yield return (nameof(BundlePackEditorDialog), () => new BundlePackEditorDialog());
        yield return (nameof(FileManagerDialog), () => new FileManagerDialog());
        yield return (nameof(ProjectItemPickerDialog), () => new ProjectItemPickerDialog());
        yield return (nameof(ToolDialogWindow), () => new ToolDialogWindow());
        yield return (nameof(ShareProfileDialogWindow), () => new ShareProfileDialogWindow());
        yield return (nameof(ImportProfileInspectionWindow), () => new ImportProfileInspectionWindow());
        yield return (nameof(ImportSubscriptionDialog), () => new ImportSubscriptionDialog());
        yield return (nameof(UpdateNotificationWindow), () => new UpdateNotificationWindow());
        yield return (nameof(SetupWizardView), () => new SetupWizardView());
        yield return (nameof(AddLocalContentWindow), () => new AddLocalContentWindow());
        yield return (nameof(GameProfileSettingsWindow), () => new GameProfileSettingsWindow());
        yield return (nameof(SubscriptionConfirmationDialog), () => new SubscriptionConfirmationDialog());
        yield return (nameof(ProfileSelectionView), () => new ProfileSelectionView());
        yield return (nameof(DependencyPreviewView), () => new DependencyPreviewView());
        yield return (nameof(GameClientSelectionView), () => new GameClientSelectionView());
    }

    private sealed class DisposeOptInWindow : GenHubWindow
    {
        protected override bool DisposeDataContextOnClose => true;
    }

    private sealed class DisposableViewModel : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
