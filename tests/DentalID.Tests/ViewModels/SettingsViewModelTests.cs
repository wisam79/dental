using Xunit;
using Moq;
using DentalID.Desktop.ViewModels;
using DentalID.Desktop.Services;

using DentalID.Application.Interfaces; // Add missing namespace

namespace DentalID.Tests.ViewModels;

public class SettingsViewModelTests
{
    private readonly Mock<IThemeService> _mockThemeService;
    private readonly Mock<IBulkOperationsService> _mockBulkService;
    private readonly Mock<ISettingsService> _mockSettingsService;
    private readonly SettingsViewModel _viewModel;

    public SettingsViewModelTests()
    {
        _mockThemeService = new Mock<IThemeService>();
        _mockThemeService.Setup(ts => ts.CurrentThemeName).Returns("Light");

        _mockBulkService = new Mock<IBulkOperationsService>();
        _mockSettingsService = new Mock<ISettingsService>();

        _viewModel = new SettingsViewModel(_mockThemeService.Object, _mockBulkService.Object, _mockSettingsService.Object);
    }

    [Fact]
    public void Constructor_ShouldInitializePropertiesCorrectly()
    {
        // Assert
        Assert.Equal("Settings", _viewModel.Title);
        Assert.Equal(1, _viewModel.SelectedThemeIndex); // Light = 1
    }

    [Fact]
    public void SelectedThemeIndex_ShouldUpdateThemeService_AndSaveSettings()
    {
        // Act
        _viewModel.SelectedThemeIndex = 0; // Dark

        // Assert
        _mockThemeService.Verify(ts => ts.ApplyTheme("Dark"), Times.Once);
        _mockSettingsService.VerifySet(s => s.Theme = "Dark", Times.Once);
        _mockSettingsService.Verify(s => s.Save(), Times.Once);
    }

    [Fact]
    public void SelectedThemeIndex_HighContrast_ShouldApplyHighContrast()
    {
        // Act
        _viewModel.SelectedThemeIndex = 2; // HighContrast

        // Assert
        _mockThemeService.Verify(ts => ts.ApplyTheme("HighContrast"), Times.Once);
        _mockSettingsService.VerifySet(s => s.Theme = "HighContrast", Times.Once);
        _mockSettingsService.Verify(s => s.Save(), Times.Once);
    }
}
