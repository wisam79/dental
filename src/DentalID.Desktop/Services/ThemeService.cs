using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace DentalID.Desktop.Services;

/// <summary>
/// Manages runtime theme switching by swapping resource dictionaries.
/// </summary>
public class ThemeService : IThemeService
{
    private readonly Avalonia.Application? _app;
    private readonly AppSettings _settings;
    private ResourceDictionary? _currentTheme;

    public string CurrentThemeName { get; private set; }

    public ThemeService(Avalonia.Application? app, AppSettings settings)
    {
        _app = app;
        _settings = settings;
        CurrentThemeName = settings.Theme ?? "Dark";
    }

    /// <summary>
    /// Applies a theme by name: "Dark", "Light", or "HighContrast".
    /// </summary>
    public void ApplyTheme(string themeName)
    {
        // Load and apply new theme
        var uri = themeName switch
        {
            "Light" => new Uri("avares://DentalID.Desktop/Themes/LightTheme.axaml"),
            "HighContrast" => new Uri("avares://DentalID.Desktop/Themes/HighContrastTheme.axaml"),
            _ => new Uri("avares://DentalID.Desktop/Themes/DarkTheme.axaml")
        };

        if (_app != null)
        {
            // Remove old theme dictionary
            if (_currentTheme != null)
            {
                _app.Resources.MergedDictionaries.Remove(_currentTheme);
            }
            
            var dict = (ResourceDictionary)AvaloniaXamlLoader.Load(uri);
            _app.Resources.MergedDictionaries.Add(dict);
            _currentTheme = dict;

            // Set Avalonia theme variant for FluentTheme compatibility
            _app.RequestedThemeVariant = themeName switch
            {
                "Light" => ThemeVariant.Light,
                _ => ThemeVariant.Dark
            };
        }

        CurrentThemeName = themeName;
        _settings.Theme = themeName;
        _settings.Save();
    }
}
