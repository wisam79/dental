using Xunit;
using FluentAssertions;
using DentalID.Desktop.ViewModels;

namespace DentalID.Tests.Desktop.ViewModels;

public class ImageComparisonViewModelTests
{
    [Fact]
    public void Constructor_InitializesTitleCorrectly()
    {
        // Arrange & Act
        var sut = new ImageComparisonViewModel();

        // Assert
        sut.Title.Should().Be("Image Comparison");
    }

    [Fact]
    public void ResetViewCommand_ClearsImagePaths()
    {
        // Arrange
        var sut = new ImageComparisonViewModel();
        sut.Image1Path = "some/path/image1.jpg";
        sut.Image2Path = "some/path/image2.jpg";

        // Act
        sut.ResetViewCommand.Execute(null);

        // Assert
        sut.Image1Path.Should().BeNull();
        sut.Image2Path.Should().BeNull();
    }
}