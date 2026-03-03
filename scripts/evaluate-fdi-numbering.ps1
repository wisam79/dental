param(
    [string]$DbPath = "dentalid.db",
    [int]$Take = 200,
    [double]$MinConfidence = 0.0,
    [string]$TruthCsv = "",
    [string]$OutCsv = "fdi_audit.csv",
    [string]$AppSettings = "src/DentalID.Desktop/appsettings.json"
)

$project = Join-Path $PSScriptRoot "FdiNumberingAudit/FdiNumberingAudit.csproj"
if (-not (Test-Path $project)) {
    throw "Audit project not found: $project"
}

$args = @(
    "run",
    "--project", $project,
    "--",
    "--db", $DbPath,
    "--take", $Take,
    "--min-confidence", $MinConfidence.ToString([System.Globalization.CultureInfo]::InvariantCulture),
    "--appsettings", $AppSettings,
    "--out", $OutCsv
)

if (-not [string]::IsNullOrWhiteSpace($TruthCsv)) {
    $args += @("--truth", $TruthCsv)
}

dotnet @args
