using DentalID.Core.DTOs;
using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using FluentValidation;

namespace DentalID.Core.Validators;

/// <summary>
/// Validator for AnalysisResult DTO
/// </summary>
public class AnalysisResultValidator : AbstractValidator<AnalysisResult>
{
    public AnalysisResultValidator()
    {
        RuleFor(x => x.Teeth)
            .NotNull()
            .WithMessage("Teeth collection cannot be null");

        RuleForEach(x => x.Teeth)
            .SetValidator(new DetectedToothValidator());

        RuleFor(x => x.Pathologies)
            .NotNull()
            .WithMessage("Pathologies collection cannot be null");

        RuleForEach(x => x.Pathologies)
            .SetValidator(new DetectedPathologyValidator());

        RuleFor(x => x.Flags)
            .NotNull()
            .WithMessage("Flags collection cannot be null");

        // Validate estimated age range
        RuleFor(x => x.EstimatedAge)
            .InclusiveBetween(0, 120)
            .When(x => x.EstimatedAge.HasValue)
            .WithMessage("Estimated age must be between 0 and 120");

        // Validate estimated gender
        RuleFor(x => x.EstimatedGender)
            .MaximumLength(20)
            .When(x => !string.IsNullOrEmpty(x.EstimatedGender))
            .WithMessage("Estimated gender must not exceed 20 characters");
    }
}

/// <summary>
/// Validator for DetectedTooth DTO
/// </summary>
public class DetectedToothValidator : AbstractValidator<DetectedTooth>
{
    public DetectedToothValidator()
    {
        RuleFor(x => x.FdiNumber)
            .InclusiveBetween(11, 48)
            .WithMessage("FDI tooth number must be between 11 and 48");

        RuleFor(x => x.Confidence)
            .InclusiveBetween(0f, 1f)
            .WithMessage("Confidence must be between 0 and 1");

        // Validate bounding box coordinates
        RuleFor(x => x.X)
            .GreaterThanOrEqualTo(0)
            .WithMessage("X coordinate must be non-negative");

        RuleFor(x => x.Y)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Y coordinate must be non-negative");

        RuleFor(x => x.Width)
            .GreaterThan(0)
            .WithMessage("Width must be positive");

        RuleFor(x => x.Height)
            .GreaterThan(0)
            .WithMessage("Height must be positive");
    }
}

/// <summary>
/// Validator for DetectedPathology DTO
/// </summary>
public class DetectedPathologyValidator : AbstractValidator<DetectedPathology>
{
    private static readonly string[] ValidPathologyTypes = 
    {
        "Caries", "Crown", "Filling", "Implant", "Missing teeth",
        "Periapical lesion", "Root Piece", "Root canal obturation"
    };

    public DetectedPathologyValidator()
    {
        RuleFor(x => x.ToothNumber)
            .InclusiveBetween(11, 48)
            .When(x => x.ToothNumber.HasValue)
            .WithMessage("Tooth number must be between 11 and 48");

        RuleFor(x => x.ClassName)
            .NotEmpty()
            .WithMessage("Pathology class name is required")
            .MaximumLength(50)
            .WithMessage("Class name must not exceed 50 characters");

        RuleFor(x => x.Confidence)
            .InclusiveBetween(0f, 1f)
            .WithMessage("Confidence must be between 0 and 1");
    }
}

/// <summary>
/// Validator for DentalFingerprint DTO
/// </summary>
public class DentalFingerprintValidator : AbstractValidator<DentalFingerprint>
{
    public DentalFingerprintValidator()
    {
        RuleFor(x => x.Code)
            .MaximumLength(500)
            .When(x => !string.IsNullOrEmpty(x.Code))
            .WithMessage("Fingerprint code must not exceed 500 characters");

        RuleFor(x => x.UniquenessScore)
            .InclusiveBetween(0, 100)
            .When(x => x.UniquenessScore > 0)
            .WithMessage("Uniqueness score must be between 0 and 100");

        RuleFor(x => x.ToothMap)
            .Must(map => map == null || map.Count <= 32)
            .When(x => x.ToothMap != null)
            .WithMessage("Tooth map cannot contain more than 32 teeth");
    }
}

/// <summary>
/// Validator for MatchingCriteria DTO
/// </summary>
public class MatchingCriteriaValidator : AbstractValidator<MatchingCriteria>
{
    public MatchingCriteriaValidator()
    {
        RuleFor(x => x.Gender)
            .MaximumLength(20)
            .When(x => !string.IsNullOrEmpty(x.Gender))
            .WithMessage("Gender must not exceed 20 characters");

        RuleFor(x => x.MinAge)
            .InclusiveBetween(0, 120)
            .When(x => x.MinAge.HasValue)
            .WithMessage("Minimum age must be between 0 and 120");

        RuleFor(x => x.MaxAge)
            .InclusiveBetween(0, 120)
            .When(x => x.MaxAge.HasValue)
            .WithMessage("Maximum age must be between 0 and 120");

        RuleFor(x => x)
            .Must(x => !x.MinAge.HasValue || !x.MaxAge.HasValue || x.MinAge <= x.MaxAge)
            .When(x => x.MinAge.HasValue && x.MaxAge.HasValue)
            .WithMessage("Minimum age must be less than or equal to maximum age");
    }
}

/// <summary>
/// Validator for Subject entity
/// </summary>
public class SubjectValidator : AbstractValidator<Subject>
{
    public SubjectValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty()
            .WithMessage("Full name is required")
            .MaximumLength(200)
            .WithMessage("Full name must not exceed 200 characters");

        RuleFor(x => x.Gender)
            .MaximumLength(20)
            .When(x => !string.IsNullOrEmpty(x.Gender))
            .WithMessage("Gender must not exceed 20 characters");

        RuleFor(x => x.DateOfBirth)
            .LessThanOrEqualTo(DateTime.UtcNow)
            .When(x => x.DateOfBirth.HasValue)
            .WithMessage("Date of birth cannot be in the future");
    }
}

/// <summary>
/// Validator for AnalysisContext (AI Chat context)
/// </summary>
public class AnalysisContextValidator : AbstractValidator<AnalysisContext>
{
    public AnalysisContextValidator()
    {
        RuleFor(x => x.TeethCount)
            .InclusiveBetween(0, 32)
            .WithMessage("Teeth count must be between 0 and 32");

        RuleFor(x => x.PathologiesCount)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Pathologies count must be non-negative");

        RuleFor(x => x.EstimatedAge)
            .InclusiveBetween(0, 120)
            .When(x => x.EstimatedAge.HasValue)
            .WithMessage("Estimated age must be between 0 and 120");

        RuleFor(x => x.EstimatedGender)
            .MaximumLength(20)
            .When(x => !string.IsNullOrEmpty(x.EstimatedGender))
            .WithMessage("Estimated gender must not exceed 20 characters");

        RuleFor(x => x.UniquenessScore)
            .InclusiveBetween(0.0, 1.0)
            .When(x => x.UniquenessScore.HasValue)
            .WithMessage("Uniqueness score must be between 0 and 1");

        RuleFor(x => x.ProcessingTimeMs)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Processing time must be non-negative");

        RuleForEach(x => x.DetectedPathologies)
            .MaximumLength(50)
            .When(x => x.DetectedPathologies != null)
            .WithMessage("Pathology description must not exceed 50 characters");

        RuleForEach(x => x.SmartInsights)
            .MaximumLength(500)
            .When(x => x.SmartInsights != null)
            .WithMessage("Insight must not exceed 500 characters");
    }
}
