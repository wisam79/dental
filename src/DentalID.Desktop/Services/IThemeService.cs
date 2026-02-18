namespace DentalID.Desktop.Services;

public interface IThemeService
{
    string CurrentThemeName { get; }
    void ApplyTheme(string themeName);
}
