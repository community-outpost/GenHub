using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenHub.Core.Constants;
using GenHub.Features.GameProfiles.ViewModels.Wizard;
using GenHub.Features.GameProfiles.Views;
using GenHub.Features.GameProfiles.Views.Wizard;
using GenHub.Tests.Core.Collections;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles.Views;

/// <summary>Exercises shared actions in profile flows while retaining their distinct selection controls.</summary>
[Collection(LocalizationCultureCollection.Name)]
public class ProfileActionStyleTests
{
    /// <summary>The wizard footer uses action roles and still confirms the selected setup choices.</summary>
    [AvaloniaFact]
    public void WizardFooter_RetainsConfirmAndCancelCommands()
    {
        var viewModel = new SetupWizardViewModel([]);
        var window = new SetupWizardView { DataContext = viewModel };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var buttons = window.GetVisualDescendants().OfType<Button>().ToArray();
            var confirm = Assert.Single(buttons, button => button.Classes.Contains("action-primary"));
            var cancel = Assert.Single(buttons, button => button.Classes.Contains("action-secondary"));
            Assert.Same(viewModel.ConfirmCommand, confirm.Command);
            Assert.Same(viewModel.CancelCommand, cancel.Command);
            confirm.Command!.Execute(confirm.CommandParameter);
            Assert.True(viewModel.Confirmed);
            cancel.Command!.Execute(cancel.CommandParameter);
            Assert.False(viewModel.Confirmed);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Local content utility controls retain named circular targets and the main action role.</summary>
    [AvaloniaFact]
    public void AddLocalContent_UsesAccessibleUtilityActions()
    {
        var resources = Avalonia.Application.Current!.Resources;
        var hadPrevious = resources.TryGetValue(LocalizationConstants.ResourceServiceKey, out var previous);
        resources[LocalizationConstants.ResourceServiceKey] = new MarkerLocalizationService();
        var view = new AddLocalContentView();
        var window = new Window { Width = 900, Height = 650, Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var buttons = view.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.Single(buttons, button => button.Classes.Contains("action-primary"));
            var utilities = buttons.Where(button => button.Classes.Contains("icon-action")).ToArray();

            // The empty file tree mounts only the folder and file browse utilities.
            Assert.Equal(2, utilities.Length);
            Assert.All(utilities, button =>
            {
                Assert.True(button.MinWidth >= 32);
                Assert.True(button.MinHeight >= 32);
                Assert.False(string.IsNullOrWhiteSpace(button.GetValue(Avalonia.Automation.AutomationProperties.NameProperty)));
            });
        }
        finally
        {
            window.Close();
            if (hadPrevious)
            {
                resources[LocalizationConstants.ResourceServiceKey] = previous;
            }
            else
            {
                resources.Remove(LocalizationConstants.ResourceServiceKey);
            }
        }
    }
}
