# FDI Numbering Audit

Tool to evaluate DentalID tooth-numbering capability from saved analysis records in `dentalid.db`.

## Run

```powershell
dotnet run --project scripts/FdiNumberingAudit/FdiNumberingAudit.csproj -- --db .\dentalid.db --take 300 --out .\fdi_audit.csv
```

## Optional ground truth

If you have verified labels, pass a CSV:

- Header: `Id,ExpectedFdis`
- Example row: `128,11;12;13;14;15;16;17;18;21;22`

```powershell
dotnet run --project scripts/FdiNumberingAudit/FdiNumberingAudit.csproj -- --db .\dentalid.db --truth .\truth.csv
```

## Metrics reported

- Coverage of full adult dentition (32)
- FDI validity rate
- Duplicate FDI rate
- Quadrant continuity and arch balance
- Capability score (/100)
