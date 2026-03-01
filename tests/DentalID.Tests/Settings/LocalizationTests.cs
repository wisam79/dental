using System.Collections.Generic;
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

        var loc = Loc.Instance;
        try
        {
            // Act
            // Switch Loc to AR externally
            loc.SwitchLanguage("ar");
            
            // Re-instantiate VM to see if it picks up correct index (simulate page reload or nav)
            var vm2 = new SettingsViewModel(themeServiceMock.Object, bulkServiceMock.Object, new Moq.Mock<ISettingsService>().Object);
            
            // Assert
            vm2.SelectedLanguageIndex.Should().Be(1); // 1 = Arabic

            // Act - Change VM property
            vm2.SelectedLanguageIndex = 0; // English

            // Assert
            loc.CurrentLanguage.Should().Be("en");
            loc.IsRtl.Should().BeFalse();
        }
        finally
        {
            loc.SwitchLanguage("en");
        }
    }

    [Fact]
    public void SwitchLanguage_ShouldRaiseExpectedPropertyChanges_AndToggleIsRtl()
    {
        var loc = Loc.Instance;
        loc.SwitchLanguage("en"); // Ensure default state before test

        var changes = new List<string>();
        void Handler(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.PropertyName))
                changes.Add(e.PropertyName!);
        }

        loc.PropertyChanged += Handler;
        try
        {
            loc.SwitchLanguage("ar");
            loc.IsRtl.Should().BeTrue();
            loc.CurrentLanguage.Should().Be("ar");

            changes.Should().Contain("Item[]");
            changes.Should().Contain(nameof(Loc.IsRtl));
            changes.Should().Contain(nameof(Loc.CurrentLanguage));

            changes.Clear();

            loc.SwitchLanguage("en");
            loc.IsRtl.Should().BeFalse();
            loc.CurrentLanguage.Should().Be("en");

            changes.Should().Contain("Item[]");
            changes.Should().Contain(nameof(Loc.IsRtl));
            changes.Should().Contain(nameof(Loc.CurrentLanguage));
        }
        finally
        {
            loc.PropertyChanged -= Handler;
            loc.SwitchLanguage("en");
        }
    }
}
