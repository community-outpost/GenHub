using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using GenHub.Common.ViewModels;

namespace GenHub.Infrastructure.UI;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null)
            return null;

        var viewName = data.GetType().FullName!.Replace("ViewModel", "View", StringComparison.InvariantCulture);
        var type = Type.GetType(viewName);

        if (type is null)
        {
            return new TextBlock
            {
                Text = "Couldn't find view: " + viewName
            };
        }

        var control = (Control)Activator.CreateInstance(type)!;
        control.DataContext = data;
        return control;
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
