using DentalID.Core.DTOs;
using FluentValidation;

namespace DentalID.Core.Validators;

/// <summary>
/// Bug #15 fix: Extract shared rules into a base class to avoid duplication (DRY principle).
/// </summary>
public abstract class SubjectValidatorBase<T> : AbstractValidator<T>
{
    protected void ApplyCommonRules(
        IRuleBuilderInitial<T, string> fullNameRule,
        IRuleBuilderInitial<T, string?> genderRule,
        IRuleBuilderInitial<T, string?> nationalIdRule,
        IRuleBuilderInitial<T, string?> contactInfoRule,
        IRuleBuilderInitial<T, string?> notesRule,
        IRuleBuilderInitial<T, DateTime?> dateOfBirthRule)
    {
        fullNameRule
            .NotEmpty().WithMessage("Full name is required")
            .MaximumLength(200).WithMessage("Full name cannot exceed 200 characters");

        // Bug #13 fix: Use case-insensitive comparison for Gender values
        genderRule
            .MaximumLength(10).WithMessage("Gender cannot exceed 10 characters")
            .Must(g => g == null || new[] { "Male", "Female", "Other" }
                .Contains(g, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Gender must be Male, Female, or Other");

        nationalIdRule
            .MaximumLength(50).WithMessage("National ID cannot exceed 50 characters");

        contactInfoRule
            .MaximumLength(500).WithMessage("Contact info cannot exceed 500 characters");

        notesRule
            .MaximumLength(1000).WithMessage("Notes cannot exceed 1000 characters");

        // Bug #14 fix: Use DateTime.Today (local date only) instead of DateTime.UtcNow to avoid timezone issues
        // Bug #16 fix: Add .When(x => ...) guard to avoid applying rules when DateOfBirth is null
        dateOfBirthRule
            .LessThanOrEqualTo(_ => DateTime.Today).WithMessage("Date of birth cannot be in the future")
            .GreaterThan(new DateTime(1900, 1, 1)).WithMessage("Date of birth must be after 1900")
            .When(dto => GetDateOfBirth(dto).HasValue);
    }

    // Override in subclasses to provide the DateOfBirth accessor
    protected abstract DateTime? GetDateOfBirth(T dto);
}

/// <summary>
/// Validator for CreateSubjectDto
/// </summary>
public class CreateSubjectValidator : SubjectValidatorBase<CreateSubjectDto>
{
    public CreateSubjectValidator()
    {
        ApplyCommonRules(
            RuleFor(x => x.FullName),
            RuleFor(x => x.Gender),
            RuleFor(x => x.NationalId),
            RuleFor(x => x.ContactInfo),
            RuleFor(x => x.Notes),
            RuleFor(x => x.DateOfBirth));
    }

    protected override DateTime? GetDateOfBirth(CreateSubjectDto dto) => dto.DateOfBirth;
}

/// <summary>
/// Validator for UpdateSubjectDto
/// Bug #15 fix: Reuses CreateSubjectValidator rules instead of duplicating them
/// </summary>
public class UpdateSubjectValidator : SubjectValidatorBase<UpdateSubjectDto>
{
    public UpdateSubjectValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0).WithMessage("Subject ID must be greater than 0");

        ApplyCommonRules(
            RuleFor(x => x.FullName),
            RuleFor(x => x.Gender),
            RuleFor(x => x.NationalId),
            RuleFor(x => x.ContactInfo),
            RuleFor(x => x.Notes),
            RuleFor(x => x.DateOfBirth));
    }

    protected override DateTime? GetDateOfBirth(UpdateSubjectDto dto) => dto.DateOfBirth;
}
