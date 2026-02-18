namespace DentalID.Application.Interfaces;

public interface ISettingsService
{
    string Theme { get; set; }
    string Language { get; set; }
    void Save();
    void Load();
}
