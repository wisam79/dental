using Xunit;
using FluentAssertions;
using DentalID.Desktop.ViewModels;

namespace DentalID.Tests.Desktop.ViewModels;

public class ReportGeneratorViewModelTests
{
    [Fact]
    public void Constructor_InitializesTitleCorrectly()
    {
        // Arrange & Act
        var sut = new ReportGeneratorViewModel();

        // Assert
        sut.Title.Should().Be("Report Generator");
    }
}