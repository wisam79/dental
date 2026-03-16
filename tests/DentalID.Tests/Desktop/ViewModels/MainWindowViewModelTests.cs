using Xunit;
using FluentAssertions;
using Moq;
using DentalID.Desktop.ViewModels;
using DentalID.Desktop.Services;
using DentalID.Core.Interfaces;

namespace DentalID.Tests.Desktop.ViewModels;

public class MainWindowViewModelTests
{
    [Fact]
    public void NavigateToComparison_SetsCorrectIndex()
    {
        // Arrange
        var mockNav = new Mock<INavigationService>();
        var mockToast = new Mock<IToastService>();
        var mockLogger = new Mock<ILoggerService>();
        var sut = new MainWindowViewModel(mockNav.Object, mockToast.Object, mockLogger.Object);

        // Act
        sut.NavigateToComparisonCommand.Execute(null);

        // Assert
        sut.SelectedNavIndex.Should().Be(3);
    }

    [Fact]
    public void NavigateToReports_SetsCorrectIndex()
    {
        // Arrange
        var mockNav = new Mock<INavigationService>();
        var mockToast = new Mock<IToastService>();
        var mockLogger = new Mock<ILoggerService>();
        var sut = new MainWindowViewModel(mockNav.Object, mockToast.Object, mockLogger.Object);

        // Act
        sut.NavigateToReportsCommand.Execute(null);

        // Assert
        sut.SelectedNavIndex.Should().Be(4);
    }
}