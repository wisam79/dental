using DentalID.Core.DTOs;
using DentalID.Core.Enums;
using FluentValidation;

namespace DentalID.Core.Validators;

/// <summary>
/// Validator for CreateCaseDto
/// </summary>
public class CreateCaseValidator : AbstractValidator<CreateCaseDto>
{
    public CreateCaseValidator()
    {
        RuleFor(x => x.CaseNumber)
            .NotEmpty().WithMessage("Case number is required")
            .MaximumLength(50).WithMessage("Case number cannot exceed 50 characters");

        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Case title is required")
            .MaximumLength(300).WithMessage("Case title cannot exceed 300 characters");

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Case description cannot exceed 2000 characters")
            .When(x => x.Description != null);

        RuleFor(x => x.CaseType)
            .MaximumLength(100).WithMessage("Case type cannot exceed 100 characters")
            .When(x => x.CaseType != null);

        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Invalid case status");

        RuleFor(x => x.Priority)
            .IsInEnum().WithMessage("Invalid case priority");

        RuleFor(x => x.ReportedBy)
            .MaximumLength(200).WithMessage("Reported by cannot exceed 200 characters")
            .When(x => x.ReportedBy != null);

        RuleFor(x => x.Location)
            .MaximumLength(500).WithMessage("Location cannot exceed 500 characters")
            .When(x => x.Location != null);

        RuleFor(x => x.EvidenceCount)
            .GreaterThanOrEqualTo(0).WithMessage("Evidence count must be non-negative");

        RuleFor(x => x.Result)
            .MaximumLength(1000).WithMessage("Case result cannot exceed 1000 characters")
            .When(x => x.Result != null);

        RuleFor(x => x.IncidentDate)
            .LessThanOrEqualTo(DateTime.UtcNow).WithMessage("Incident date cannot be in the future")
            .GreaterThan(new DateTime(1900, 1, 1)).WithMessage("Incident date must be after 1900")
            .When(x => x.IncidentDate.HasValue);
    }
}

/// <summary>
/// Validator for UpdateCaseDto
/// </summary>
public class UpdateCaseValidator : AbstractValidator<UpdateCaseDto>
{
    public UpdateCaseValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0).WithMessage("Case ID must be greater than 0");

        RuleFor(x => x.CaseNumber)
            .NotEmpty().WithMessage("Case number is required")
            .MaximumLength(50).WithMessage("Case number cannot exceed 50 characters");

        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Case title is required")
            .MaximumLength(300).WithMessage("Case title cannot exceed 300 characters");

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Case description cannot exceed 2000 characters")
            .When(x => x.Description != null);

        RuleFor(x => x.CaseType)
            .MaximumLength(100).WithMessage("Case type cannot exceed 100 characters")
            .When(x => x.CaseType != null);

        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Invalid case status");

        RuleFor(x => x.Priority)
            .IsInEnum().WithMessage("Invalid case priority");

        RuleFor(x => x.ReportedBy)
            .MaximumLength(200).WithMessage("Reported by cannot exceed 200 characters")
            .When(x => x.ReportedBy != null);

        RuleFor(x => x.Location)
            .MaximumLength(500).WithMessage("Location cannot exceed 500 characters")
            .When(x => x.Location != null);

        RuleFor(x => x.EvidenceCount)
            .GreaterThanOrEqualTo(0).WithMessage("Evidence count must be non-negative");

        RuleFor(x => x.Result)
            .MaximumLength(1000).WithMessage("Case result cannot exceed 1000 characters")
            .When(x => x.Result != null);

        RuleFor(x => x.IncidentDate)
            .LessThanOrEqualTo(DateTime.UtcNow).WithMessage("Incident date cannot be in the future")
            .GreaterThan(new DateTime(1900, 1, 1)).WithMessage("Incident date must be after 1900")
            .When(x => x.IncidentDate.HasValue);
    }
}
