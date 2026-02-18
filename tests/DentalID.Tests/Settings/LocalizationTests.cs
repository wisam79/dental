using System.Globalization;
using DentalID.Desktop.Services;
using DentalID.Desktop.ViewModels;
using FluentAssertions;
using Xunit;

using DentalID.Application.Interfaces; // Add missing namespace

namespace DentalID.Tests.Settings;

public class LocalizationTests
{
    // ... items ...

    [Fact]
    public void SettingsViewModel_ShouldSyncWithLoc()
    {
        // Arrange
        var themeServiceMock = new Moq.Mock<IThemeService>();
        themeServiceMock.Setup(x => x.CurrentThemeName).Returns("Dark");
        
        var bulkServiceMock = new Moq.Mock<IBulkOperationsService>();

        var vm = new SettingsViewModel(themeServiceMock.Object, bulkServiceMock.Object, new Moq.Mock<ISettingsService>().Object);

        // Act
        // Switch Loc to AR externally
        Loc.Instance.SwitchLanguage("ar");
        
        // Re-instantiate VM to see if it picks up correct index (simulate page reload or nav)
        var vm2 = new SettingsViewModel(themeServiceMock.Object, bulkServiceMock.Object, new Moq.Mock<ISettingsService>().Object);
        
        // Assert
        vm2.SelectedLanguageIndex.Should().Be(1); // 1 = Arabic

        // Act - Change VM property
        vm2.SelectedLanguageIndex = 0; // English

        // Assert
        Loc.Instance.CurrentLanguage.Should().Be("en");
        Loc.Instance.IsRtl.Should().BeFalse();
    }
}
