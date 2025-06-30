using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using GenHub.Common.ViewModels;

namespace GenHub;

/// <summary>
/// ViewLocator is used to find the correct view for a given ViewModel.
/// </summary>
public class ViewLocator : IDataTemplate
{
    /// <inheritdoc/>
    public Control? Build(object? data)
    {
        if (data is null)
            return null;

        var viewName = data.GetType().FullName!.Replace("ViewModel", "View", StringComparison.InvariantCulture);
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.FullName == viewName);

        if (type is null)
        {
            return new TextBlock
            {
                Text = "Couldn't find view: " + viewName,
            };
        }

        var control = (Control)Activator.CreateInstance(type)!;
        control.DataContext = data;
        return control;
    }

    /// <inheritdoc/>
    public bool Match(object? data)
    {
        return data is ViewModelBase || (data?.GetType().Name.EndsWith("ViewModel") ?? false);
    }
}
