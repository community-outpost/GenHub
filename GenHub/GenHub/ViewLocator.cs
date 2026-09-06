using System;
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
        {
            return null;
        }

        var viewName = data.GetType().FullName!.Replace("ViewModel", "View", StringComparison.InvariantCulture);
        var type = typeof(App).Assembly.GetType(viewName);

        if (type is null)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = assembly.GetType(viewName);
                    if (type is not null)
                    {
                        break;
                    }
                }
                catch
                {
                    // Ignore assembly scan errors for unloaded dependencies
                }
            }
        }

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
        return data is ViewModelBase || data?.GetType().Name.EndsWith("ViewModel", StringComparison.Ordinal) == true;
    }
}