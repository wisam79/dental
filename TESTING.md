# DentalID Testing Guide

This project uses a single test project with suite filters through `scripts/test.ps1`.

## Prerequisites

1. .NET 10 SDK
2. PowerShell 7+ (`pwsh`)
3. Restored tools:

```powershell
dotnet tool restore
```

## Run all suites

```powershell
pwsh ./scripts/test.ps1 -Suite all -Configuration Release
```

## Run a single suite

```powershell
pwsh ./scripts/test.ps1 -Suite unit -Configuration Release
pwsh ./scripts/test.ps1 -Suite integration -Configuration Release
pwsh ./scripts/test.ps1 -Suite e2e -Configuration Release
pwsh ./scripts/test.ps1 -Suite performance -Configuration Release
```

## Coverage

```powershell
pwsh ./scripts/test.ps1 -Suite all -Configuration Release -CollectCoverage
```

Coverage output is written to `TestResults/Coverage/`.

## Known failures mode

Use this mode only when validating against the explicit allow-list in `tests/known-test-failures.txt`:

```powershell
pwsh ./scripts/test.ps1 -Suite all -Configuration Release -AllowKnownFailures
```

Unexpected failures still fail the run.

## Direct dotnet test

```powershell
dotnet test tests/DentalID.Tests/DentalID.Tests.csproj -c Release
```

For deterministic behavior, parallel execution is disabled at assembly level.
