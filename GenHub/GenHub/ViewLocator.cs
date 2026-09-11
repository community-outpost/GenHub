using System;
using System.IO;
using System.Reflection;
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
        var type = typeof(App).Assembly.GetType(viewName) ?? ResolveTypeFromAppDomain(viewName);

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

    /// <summary>
    /// Attempts to resolve a view type by name across loaded assemblies in the current AppDomain.
    /// </summary>
    /// <param name="viewName">The full type name of the view.</param>
    /// <returns>The resolved <see cref="Type"/>, or <c>null</c> if not found.</returns>
    private static Type? ResolveTypeFromAppDomain(string viewName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var type = assembly.GetType(viewName);
                if (type is not null)
                {
                    return type;
                }
            }
            catch (TypeLoadException)
            {
                // Ignore assembly scan errors for unloaded dependencies
            }
            catch (FileNotFoundException)
            {
                // Ignore assembly scan errors for unloaded dependencies
            }
            catch (ReflectionTypeLoadException)
            {
                // Ignore assembly scan errors for unloaded dependencies
            }
        }

        return null;
    }
}
