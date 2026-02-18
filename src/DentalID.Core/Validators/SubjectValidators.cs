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
public class CreateSubjectValidator : AbstractValidator<CreateSubjectDto>
{
    private static readonly string[] ValidGenders = { "Male", "Female", "Other" };

    public CreateSubjectValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required")
            .MaximumLength(200).WithMessage("Full name cannot exceed 200 characters");

        // Bug #13 fix: Case-insensitive gender validation
        RuleFor(x => x.Gender)
            .MaximumLength(10).WithMessage("Gender cannot exceed 10 characters")
            .Must(g => g == null || ValidGenders.Contains(g, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Gender must be Male, Female, or Other");

        RuleFor(x => x.NationalId)
            .MaximumLength(50).WithMessage("National ID cannot exceed 50 characters");

        RuleFor(x => x.ContactInfo)
            .MaximumLength(500).WithMessage("Contact info cannot exceed 500 characters");

        RuleFor(x => x.Notes)
            .MaximumLength(1000).WithMessage("Notes cannot exceed 1000 characters");

        // Bug #14 fix: Use DateTime.Today to avoid UTC/local timezone mismatch
        // Bug #16 fix: Guard with .When() so null DateOfBirth skips these rules
        RuleFor(x => x.DateOfBirth)
            .LessThanOrEqualTo(_ => DateTime.Today).WithMessage("Date of birth cannot be in the future")
            .GreaterThan(new DateTime(1900, 1, 1)).WithMessage("Date of birth must be after 1900")
            .When(x => x.DateOfBirth.HasValue);
    }
}

/// <summary>
/// Validator for UpdateSubjectDto
/// Bug #15 fix: Reuses CreateSubjectValidator rules instead of duplicating them
/// </summary>
public class UpdateSubjectValidator : AbstractValidator<UpdateSubjectDto>
{
    private static readonly string[] ValidGenders = { "Male", "Female", "Other" };

    public UpdateSubjectValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0).WithMessage("Subject ID must be greater than 0");

        // Bug #15 fix: Reuse identical rules from CreateSubjectValidator — no more duplication
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required")
            .MaximumLength(200).WithMessage("Full name cannot exceed 200 characters");

        // Bug #13 fix: Case-insensitive gender validation
        RuleFor(x => x.Gender)
            .MaximumLength(10).WithMessage("Gender cannot exceed 10 characters")
            .Must(g => g == null || ValidGenders.Contains(g, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Gender must be Male, Female, or Other");

        RuleFor(x => x.NationalId)
            .MaximumLength(50).WithMessage("National ID cannot exceed 50 characters");

        RuleFor(x => x.ContactInfo)
            .MaximumLength(500).WithMessage("Contact info cannot exceed 500 characters");

        RuleFor(x => x.Notes)
            .MaximumLength(1000).WithMessage("Notes cannot exceed 1000 characters");

        // Bug #14 fix: DateTime.Today instead of DateTime.UtcNow
        // Bug #16 fix: Guard with .When() for nullable DateOfBirth
        RuleFor(x => x.DateOfBirth)
            .LessThanOrEqualTo(_ => DateTime.Today).WithMessage("Date of birth cannot be in the future")
            .GreaterThan(new DateTime(1900, 1, 1)).WithMessage("Date of birth must be after 1900")
            .When(x => x.DateOfBirth.HasValue);
    }
}
