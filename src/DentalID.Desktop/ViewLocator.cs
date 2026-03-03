using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using DentalID.Desktop.ViewModels;
using DentalID.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DentalID.Desktop;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var name = param.GetType().FullName!
            .Replace("ViewModel", "View")
            .Replace("ViewModels", "Views");

        var logger = App.Services?.GetService<ILoggerService>();
        logger?.LogInformation($"[VIEW] ViewLocator: Looking for view '{name}' for '{param.GetType().FullName}'");

        var type = Type.GetType(name);

        if (type != null)
        {
            try
            {
                var control = (Control)Activator.CreateInstance(type)!;
                // Ensure each created view is bound to the requested ViewModel instance.
                // This avoids blank screens if the host presenter does not assign DataContext.
                control.DataContext = param;
                logger?.LogInformation($"[VIEW] ViewLocator: Created view '{type.Name}'");
                return control;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, $"Failed to create view '{name}'");
                return new TextBlock
                {
                    Text = $"Error creating view: {ex.Message}",
                    Foreground = Avalonia.Media.Brushes.Red
                };
            }
        }

        logger?.LogError(new Exception($"View not found: {name}"), $"View not found: {name}");
        return new TextBlock
        {
            Text = "View not found: " + name,
            Foreground = Avalonia.Media.Brushes.OrangeRed
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
