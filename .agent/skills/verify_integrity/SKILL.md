---
name: verify_integrity
description: Verify the digital integrity of forensic evidence and audit logs.
---

# Verify Integrity Skill

Use this skill to ensure that evidence files, database records, and logs have not been tampered with. This is critical for maintaining the Chain of Custody.

## Dependencies
- `DentalID.Core.Interfaces.IIntegrityService` (Hashing logic)
- `DentalID.Core.Entities.DentalImage` (Stores `FileHash`)
- `System.Security.Cryptography.SHA256`

## Workflow

1.  **Verify File Integrity**:
    - When loading an evidence file, calculate its SHA-256 hash.
    - Compare it against the stored `FileHash` in the database (`DentalImage.FileHash`).
    - **Mismatch**: Immediately flag as "TAMPERED" and alert the user. Do not proceed with analysis.

2.  **Verify Audit Log Chain**:
    - (If Blockchain/Ledger is implemented)
    - Verify that the `PreviousHash` of the current log entry matches the `Hash` of the previous entry.

3.  **Digital Seal Verification**:
    - Check for the presence of a digital signature or watermark on exported PDF reports.

## Example Usage (C#)

```csharp
using System.Security.Cryptography;

public bool VerifyEvidence(DentalImage evidence, string currentFilePath)
{
    if (!File.Exists(currentFilePath)) return false;

    // Use Interface Method
    var currentHash = await integrityService.ComputeFileHashAsync(currentFilePath);

    // Compare
    bool isValid = string.Equals(currentHash, evidence.FileHash, StringComparison.OrdinalIgnoreCase);

    if (!isValid)
    {
        Console.WriteLine($"[SECURITY ALERT] Hash Mismatch for Image {evidence.Id}!");
        Console.WriteLine($"Expected: {evidence.FileHash}");
        Console.WriteLine($"Actual:   {currentHash}");
    }

    return isValid;
}
```
