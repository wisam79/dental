using DentalID.Core.Interfaces;
using DentalID.Desktop.Services;
using DentalID.Desktop.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace DentalID.Tests.Navigation;

public class SettingsNavigationTests
{
    private readonly Mock<INavigationService> _navMock;
    private readonly Mock<IToastService> _toastMock;
    private readonly Mock<ILoggerService> _loggerMock;
    private readonly MainWindowViewModel _vm;

    public SettingsNavigationTests()
    {
        _navMock = new Mock<INavigationService>();
        _toastMock = new Mock<IToastService>();
        _loggerMock = new Mock<ILoggerService>();
        _vm = new MainWindowViewModel(_navMock.Object, _toastMock.Object, _loggerMock.Object);
        _vm.SettingsNavIndex = -1; // Ensure settings are initially deselected
    }

    [Fact]
    public void SelectSettings_ShouldDeselectMainNav_AndNavigate()
    {
        // Arrange
        _vm.SelectedNavIndex = 0; // Default Dashboard

        // Act - Click Settings (Index 0 of Settings ListBox)
        _vm.SettingsNavIndex = 0;

        // Assert
        _vm.SelectedNavIndex.Should().Be(-1); // Main Nav Deselected
        _navMock.Verify(n => n.NavigateTo<SettingsViewModel>(), Times.Once);
    }

    [Fact]
    public void SelectMainNav_ShouldDeselectSettings_AndNavigate()
    {
        // Arrange
        _vm.SettingsNavIndex = 0; // Settings currently selected

        // Act - Click Subjects (Index 1 of Main ListBox)
        _vm.SelectedNavIndex = 1;

        // Assert
        _vm.SettingsNavIndex.Should().Be(-1); // Settings Deselected
        _navMock.Verify(n => n.NavigateTo<SubjectsViewModel>(), Times.Once);
    }
}
