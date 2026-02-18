using DentalID.Core.DTOs;
using DentalID.Core.Enums;
using FluentValidation;

namespace DentalID.Core.Validators;

/// <summary>
/// Validator for CreateDentalImageDto
/// </summary>
public class CreateDentalImageValidator : AbstractValidator<CreateDentalImageDto>
{
    public CreateDentalImageValidator()
    {
        RuleFor(x => x.SubjectId)
            .GreaterThan(0).WithMessage("Subject ID must be greater than 0");

        RuleFor(x => x.ImagePath)
            .NotEmpty().WithMessage("Image path is required")
            .MaximumLength(500).WithMessage("Image path cannot exceed 500 characters");

        RuleFor(x => x.ImageType)
            .IsInEnum().WithMessage("Invalid image type");

        RuleFor(x => x.JawType)
            .Must(j => j == null || Enum.IsDefined(typeof(JawType), j))
            .WithMessage("Invalid jaw type");

        RuleFor(x => x.Quadrant)
            .MaximumLength(10).WithMessage("Quadrant cannot exceed 10 characters")
            .Must(q => q == null || new[] { "UpperLeft", "UpperRight", "LowerLeft", "LowerRight" }.Contains(q))
            .WithMessage("Quadrant must be UpperLeft, UpperRight, LowerLeft, or LowerRight");

        RuleFor(x => x.QualityScore)
            .InclusiveBetween(0, 100).WithMessage("Quality score must be between 0 and 100")
            .When(x => x.QualityScore.HasValue);

        RuleFor(x => x.UniquenessScore)
            .InclusiveBetween(0, 100).WithMessage("Uniqueness score must be between 0 and 100");

        RuleFor(x => x.CaptureDate)
            .LessThanOrEqualTo(DateTime.UtcNow).WithMessage("Capture date cannot be in the future")
            .GreaterThan(new DateTime(1900, 1, 1)).WithMessage("Capture date must be after 1900")
            .When(x => x.CaptureDate.HasValue);
    }
}

/// <summary>
/// Validator for UpdateDentalImageDto
/// </summary>
public class UpdateDentalImageValidator : AbstractValidator<UpdateDentalImageDto>
{
    public UpdateDentalImageValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0).WithMessage("Image ID must be greater than 0");

        RuleFor(x => x.SubjectId)
            .GreaterThan(0).WithMessage("Subject ID must be greater than 0");

        RuleFor(x => x.ImagePath)
            .NotEmpty().WithMessage("Image path is required")
            .MaximumLength(500).WithMessage("Image path cannot exceed 500 characters");

        RuleFor(x => x.ImageType)
            .IsInEnum().WithMessage("Invalid image type");

        RuleFor(x => x.JawType)
            .Must(j => j == null || Enum.IsDefined(typeof(JawType), j))
            .WithMessage("Invalid jaw type");

        RuleFor(x => x.Quadrant)
            .MaximumLength(10).WithMessage("Quadrant cannot exceed 10 characters")
            .Must(q => q == null || new[] { "UpperLeft", "UpperRight", "LowerLeft", "LowerRight" }.Contains(q))
            .WithMessage("Quadrant must be UpperLeft, UpperRight, LowerLeft, or LowerRight");

        RuleFor(x => x.QualityScore)
            .InclusiveBetween(0, 100).WithMessage("Quality score must be between 0 and 100")
            .When(x => x.QualityScore.HasValue);

        RuleFor(x => x.UniquenessScore)
            .InclusiveBetween(0, 100).WithMessage("Uniqueness score must be between 0 and 100");

        RuleFor(x => x.CaptureDate)
            .LessThanOrEqualTo(DateTime.UtcNow).WithMessage("Capture date cannot be in the future")
            .GreaterThan(new DateTime(1900, 1, 1)).WithMessage("Capture date must be after 1900")
            .When(x => x.CaptureDate.HasValue);
    }
}
