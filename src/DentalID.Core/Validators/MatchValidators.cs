using DentalID.Core.DTOs;
using FluentValidation;

namespace DentalID.Core.Validators;

/// <summary>
/// Validator for CreateMatchDto
/// </summary>
public class CreateMatchValidator : AbstractValidator<CreateMatchDto>
{
    public CreateMatchValidator()
    {
        RuleFor(x => x.CaseId)
            .GreaterThan(0).WithMessage("Case ID must be greater than 0");

        RuleFor(x => x.QueryImageId)
            .GreaterThan(0).WithMessage("Query image ID must be greater than 0");

        RuleFor(x => x.MatchedSubjectId)
            .GreaterThan(0).WithMessage("Matched subject ID must be greater than 0");

        RuleFor(x => x.ConfidenceScore)
            .InclusiveBetween(0, 100).WithMessage("Confidence score must be between 0 and 100");

        RuleFor(x => x.ResultType)
            .MaximumLength(100).WithMessage("Result type cannot exceed 100 characters")
            .When(x => x.ResultType != null);

        RuleFor(x => x.Notes)
            .MaximumLength(1000).WithMessage("Notes cannot exceed 1000 characters")
            .When(x => x.Notes != null);

        RuleFor(x => x.ConfirmedAt)
            .LessThanOrEqualTo(DateTime.UtcNow).WithMessage("Confirmed date cannot be in the future")
            .GreaterThan(new DateTime(1900, 1, 1)).WithMessage("Confirmed date must be after 1900")
            .When(x => x.ConfirmedAt.HasValue);
    }
}

/// <summary>
/// Validator for UpdateMatchDto
/// </summary>
public class UpdateMatchValidator : AbstractValidator<UpdateMatchDto>
{
    public UpdateMatchValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0).WithMessage("Match ID must be greater than 0");

        RuleFor(x => x.CaseId)
            .GreaterThan(0).WithMessage("Case ID must be greater than 0");

        RuleFor(x => x.QueryImageId)
            .GreaterThan(0).WithMessage("Query image ID must be greater than 0");

        RuleFor(x => x.MatchedSubjectId)
            .GreaterThan(0).WithMessage("Matched subject ID must be greater than 0");

        RuleFor(x => x.ConfidenceScore)
            .InclusiveBetween(0, 100).WithMessage("Confidence score must be between 0 and 100");

        RuleFor(x => x.ResultType)
            .MaximumLength(100).WithMessage("Result type cannot exceed 100 characters")
            .When(x => x.ResultType != null);

        RuleFor(x => x.Notes)
            .MaximumLength(1000).WithMessage("Notes cannot exceed 1000 characters")
            .When(x => x.Notes != null);

        RuleFor(x => x.ConfirmedAt)
            .LessThanOrEqualTo(DateTime.UtcNow).WithMessage("Confirmed date cannot be in the future")
            .GreaterThan(new DateTime(1900, 1, 1)).WithMessage("Confirmed date must be after 1900")
            .When(x => x.ConfirmedAt.HasValue);
    }
}
