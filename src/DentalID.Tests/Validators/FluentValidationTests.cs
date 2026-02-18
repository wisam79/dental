using DentalID.Core.DTOs;
using DentalID.Core.Entities;
using DentalID.Core.Validators;
using FluentValidation;
using Xunit;

namespace DentalID.Tests.Validators;

/// <summary>
/// Real tests for FluentValidation validators.
/// These tests actually validate the validation rules.
/// </summary>
public class FluentValidationTests
{
    [Fact]
    public void AnalysisResultValidator_WithValidData_ShouldNotHaveErrors()
    {
        // Arrange
        var validator = new AnalysisResultValidator();
        var model = new AnalysisResult
        {
            Teeth = new System.Collections.Generic.List<DetectedTooth>
            {
                new() { FdiNumber = 11, Confidence = 0.95f, X = 100f, Y = 200f, Width = 50f, Height = 60f }
            },
            Pathologies = new System.Collections.Generic.List<DetectedPathology>
            {
                new() { ClassName = "Caries", Confidence = 0.8f, ToothNumber = 11 }
            },
            EstimatedAge = 30,
            EstimatedGender = "Male",
            Flags = new System.Collections.Generic.List<string>()
        };

        // Act
        var result = validator.Validate(model);

        // Assert
        Assert.True(result.IsValid, $"Errors: {string.Join(", ", result.Errors.Select(e => e.ErrorMessage))}");
    }

    [Fact]
    public void AnalysisResultValidator_WithInvalidFdiNumber_ShouldHaveError()
    {
        // Arrange
        var validator = new AnalysisResultValidator();
        var model = new AnalysisResult
        {
            Teeth = new System.Collections.Generic.List<DetectedTooth>
            {
                new() { FdiNumber = 99, Confidence = 0.95f } // Invalid FDI number
            }
        };

        // Act
        var result = validator.Validate(model);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("FdiNumber"));
    }

    [Fact]
    public void AnalysisResultValidator_WithInvalidConfidence_ShouldHaveError()
    {
        // Arrange
        var validator = new AnalysisResultValidator();
        var model = new AnalysisResult
        {
            Teeth = new System.Collections.Generic.List<DetectedTooth>
            {
                new() { FdiNumber = 11, Confidence = 1.5f } // Invalid confidence > 1
            }
        };

        // Act
        var result = validator.Validate(model);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("Confidence"));
    }

    [Fact]
    public void AnalysisResultValidator_WithInvalidAge_ShouldHaveError()
    {
        // Arrange
        var validator = new AnalysisResultValidator();
        var model = new AnalysisResult
        {
            EstimatedAge = 200 // Invalid age > 120
        };

        // Act
        var result = validator.Validate(model);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "EstimatedAge");
    }

    [Fact]
    public void DetectedToothValidator_WithValidData_ShouldNotHaveErrors()
    {
        // Arrange
        var validator = new DetectedToothValidator();
        var model = new DetectedTooth
        {
            FdiNumber = 16,
            Confidence = 0.85f,
            X = 100f,
            Y = 200f,
            Width = 50f,
            Height = 60f
        };

        // Act
        var result = validator.Validate(model);

        // Assert
        Assert.True(result.IsValid, $"Errors: {string.Join(", ", result.Errors.Select(e => e.ErrorMessage))}");
    }

    [Fact]
    public void DetectedToothValidator_WithNegativeCoordinates_ShouldHaveError()
    {
        // Arrange
        var validator = new DetectedToothValidator();
        var model = new DetectedTooth
        {
            FdiNumber = 11,
            Confidence = 0.9f,
            X = -10f, // Invalid negative
            Y = 100f,
            Width = 50f,
            Height = 60f
        };

        // Act
        var result = validator.Validate(model);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "X");
    }

    [Fact]
    public void DetectedPathologyValidator_WithEmptyClassName_ShouldHaveError()
    {
        // Arrange
        var validator = new DetectedPathologyValidator();
        var model = new DetectedPathology
        {
            ClassName = "", // Invalid - empty
            Confidence = 0.8f
        };

        // Act
        var result = validator.Validate(model);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "ClassName");
    }

    [Fact]
    public void DetectedPathologyValidator_WithValidData_ShouldNotHaveErrors()
    {
        // Arrange
        var validator = new DetectedPathologyValidator();
        var model = new DetectedPathology
        {
            ClassName = "Caries",
            Confidence = 0.85f,
            ToothNumber = 36
        };

        // Act
        var result = validator.Validate(model);

        // Assert
        Assert.True(result.IsValid, $"Errors: {string.Join(", ", result.Errors.Select(e => e.ErrorMessage))}");
    }

    [Fact]
    public void DentalFingerprintValidator_WithValidData_ShouldNotHaveErrors()
    {
        // Arrange
        var validator = new DentalFingerprintValidator();
        var model = new DentalFingerprint
        {
            Code = "11:C-16:I-21:F",
            UniquenessScore = 85.5,
            ToothMap = new System.Collections.Generic.Dictionary<int, string>
            {
                { 11, "C" },
                { 16, "I" },
                { 21, "F" }
            }
        };

        // Act
        var result = validator.Validate(model);

        // Assert
        Assert.True(result.IsValid, $"Errors: {string.Join(", ", result.Errors.Select(e => e.ErrorMessage))}");
    }

    [Fact]
    public void MatchingCriteriaValidator_WithInvalidAgeRange_ShouldHaveError()
    {
        // Arrange
        var validator = new MatchingCriteriaValidator();
        var model = new MatchingCriteria
        {
            MinAge = 50,
            MaxAge = 30 // Invalid - max < min
        };

        // Act
        var result = validator.Validate(model);

        // Assert
        Assert.False(result.IsValid);
    }

    [Fact]
    public void SubjectValidator_WithFutureDateOfBirth_ShouldHaveError()
    {
        // Arrange
        var validator = new SubjectValidator();
        var model = new Subject
        {
            FullName = "John Doe",
            DateOfBirth = DateTime.UtcNow.AddYears(1) // Future date - invalid
        };

        // Act
        var result = validator.Validate(model);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "DateOfBirth");
    }

    [Fact]
    public void SubjectValidator_WithEmptyName_ShouldHaveError()
    {
        // Arrange
        var validator = new SubjectValidator();
        var model = new Subject
        {
            FullName = "" // Invalid - empty
        };

        // Act
        var result = validator.Validate(model);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "FullName");
    }

    [Fact]
    public void SubjectValidator_WithValidData_ShouldNotHaveErrors()
    {
        // Arrange
        var validator = new SubjectValidator();
        var model = new Subject
        {
            FullName = "John Doe",
            Gender = "Male",
            DateOfBirth = new DateTime(1990, 5, 15)
        };

        // Act
        var result = validator.Validate(model);

        // Assert
        Assert.True(result.IsValid, $"Errors: {string.Join(", ", result.Errors.Select(e => e.ErrorMessage))}");
    }
}
