---
name: manage_lab
description: Manage subjects, cases, and administrative lab tasks.
---

# Manage Lab Skill

Use this skill to perform CRUD operations on Subjects and Cases, archive closed cases, and generate lab performance reports.

## Dependencies
- `DentalID.Core.Interfaces.ISubjectRepository`
- `DentalID.Core.Interfaces.ICaseRepository`
- `DentalID.Core.Entities.Subject`
- `DentalID.Core.Entities.Case`

## Workflow

1.  **Onboard New Subject**:
    - Create a `Subject` entity.
    - **Mandatory Fields**: `FullName` (or "Unknown-{Date}" for John Does), `Gender` (if known).
    - **ID Generation**: Use standard format `SUB-{YYYY}-{SEQ}`.

2.  **Create Case**:
    - Link a `Subject` to a `Case`.
    - Set Status to `Open`.
    - Assign an `Investigator` (currrent user).

3.  **Archive Case**:
    - When a match is confirmed or investigation ends.
    - Set Status to `ClosedSolved` or `ClosedUnsolved`.
    - **Requirement**: Ensure all evidence is saved and hashed before closing.

4.  **Advanced: Bulk Import**:
    - **Scenario**: Importing a legacy database or ZIP export.
    - **Workflow**:
        1. Extract ZIP to temp folder.
        2. Parse `manifest.json` or folder structure (SubjectName/Image.jpg).
        3. For each folder:
            - Create `Subject`.
            - For each Image: Create `DentalImage`, Calculate Hash, Move to Storage.

5.  **Lab Reporting**:
    - Generate statistics on `OpenCases`, `AverageTurnaroundTime`, and `MatchSuccessRate`.

## Example Usage (C#)

```csharp
public async Task<Case> CreateNewInvestigation(string subjectName, ISubjectRepository subRepo, ICaseRepository caseRepo)
{
    // 1. Create Subject
    var subject = new Subject
    {
        FullName = subjectName,
        SubjectId = $"SUB-{DateTime.Now.Year}-{Guid.NewGuid().ToString().Substring(0, 4)}"
    };
    await subRepo.AddAsync(subject);

    // 2. Open Case
    var newCase = new Case
    {
        SubjectId = subject.Id,
        Status = CaseStatus.Open,
        OpenedAt = DateTime.UtcNow,
        Description = "New forensic intake"
    };
    await caseRepo.AddAsync(newCase);

    return newCase;
}
```
