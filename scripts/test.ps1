[CmdletBinding()]
param(
    [ValidateSet("all", "unit", "integration", "e2e", "performance")]
    [string]$Suite = "all",
    [string]$Configuration = "Release",
    [string]$Project = "tests/DentalID.Tests/DentalID.Tests.csproj",
    [string]$ResultsDirectory = "TestResults",
    [switch]$CollectCoverage,
    [switch]$AllowKnownFailures
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-TestResultsFromTrx {
    param([Parameter(Mandatory = $true)][string]$TrxPath)

    [xml]$trx = Get-Content -Path $TrxPath
    $results = @($trx.TestRun.Results.UnitTestResult)
    if ($results.Count -eq 0) {
        return [PSCustomObject]@{
            All    = @()
            Failed = @()
        }
    }

    $allTests = @($results | ForEach-Object { $_.testName } | Sort-Object -Unique)
    $failedTests = @($results |
        Where-Object { $_.outcome -eq "Failed" } |
        ForEach-Object { $_.testName } |
        Sort-Object -Unique)

    return [PSCustomObject]@{
        All    = $allTests
        Failed = $failedTests
    }
}

$suites = @(
    @{ Name = "unit"; Filter = "(FullyQualifiedName!~DentalID.Tests.Integration)&(FullyQualifiedName!~DentalID.Tests.E2E)&(FullyQualifiedName!~DentalID.Tests.Services.PerformanceTests)" },
    @{ Name = "integration"; Filter = "FullyQualifiedName~DentalID.Tests.Integration" },
    @{ Name = "e2e"; Filter = "FullyQualifiedName~DentalID.Tests.E2E" },
    @{ Name = "performance"; Filter = "FullyQualifiedName~DentalID.Tests.Services.PerformanceTests" }
)

$selectedSuites = if ($Suite -eq "all") {
    @($suites)
}
else {
    @($suites | Where-Object { $_.Name -eq $Suite })
}

if ($selectedSuites.Count -eq 0) {
    throw "Unknown suite '$Suite'."
}

$projectPath = (Resolve-Path -Path $Project).Path
$resultsRoot = Join-Path -Path (Get-Location) -ChildPath $ResultsDirectory
$runSettingsPath = Join-Path -Path (Get-Location) -ChildPath "tests/coverage.runsettings"
$knownFailuresPath = Join-Path -Path (Get-Location) -ChildPath "tests/known-test-failures.txt"

if (Test-Path -Path $resultsRoot) {
    Remove-Item -Path $resultsRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultsRoot -Force | Out-Null

$failedTests = @()
$executedTests = @()
$executionIssues = @()

foreach ($suiteConfig in $selectedSuites) {
    $suiteName = $suiteConfig.Name
    $suiteDir = Join-Path -Path $resultsRoot -ChildPath $suiteName
    $trxName = "$suiteName.trx"

    New-Item -ItemType Directory -Path $suiteDir -Force | Out-Null

    $args = @(
        "test", $projectPath,
        "--configuration", $Configuration,
        "--nologo",
        "--verbosity", "minimal",
        "--logger", "trx;LogFileName=$trxName",
        "--results-directory", $suiteDir,
        "--filter", $suiteConfig.Filter
    )

    if ($CollectCoverage) {
        $args += @("--collect", "XPlat Code Coverage", "--settings", $runSettingsPath)
    }

    Write-Host ">> Running $suiteName suite..."
    & dotnet @args
    $exitCode = $LASTEXITCODE

    $trxPath = Join-Path -Path $suiteDir -ChildPath $trxName
    if (Test-Path -Path $trxPath) {
        $testResults = Get-TestResultsFromTrx -TrxPath $trxPath
        $executedTests += @($testResults.All)
        $failedTests += @($testResults.Failed)
    }
    elseif ($exitCode -ne 0) {
        $executionIssues += "Suite '$suiteName' failed before generating TRX."
    }

    if ($exitCode -ne 0) {
        Write-Host "!! Suite $suiteName finished with non-zero exit code ($exitCode)."
    }
}

$failedTests = @($failedTests | Sort-Object -Unique)
$executedTests = @($executedTests | Sort-Object -Unique)

if ($CollectCoverage) {
    $coverageFiles = @(Get-ChildItem -Path $resultsRoot -Recurse -Filter "coverage.cobertura.xml" -File)
    if ($coverageFiles.Count -gt 1) {
        $coverageFiles = @(
            $coverageFiles |
                Group-Object {
                    (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash
                } |
                ForEach-Object { $_.Group | Select-Object -First 1 }
        )
    }

    if ($coverageFiles.Count -gt 0) {
        Write-Host ">> Generating coverage report..."
        & dotnet tool restore
        if ($LASTEXITCODE -ne 0) {
            $executionIssues += "dotnet tool restore failed."
        }

        $coverageReportDir = Join-Path -Path $resultsRoot -ChildPath "Coverage"
        New-Item -ItemType Directory -Path $coverageReportDir -Force | Out-Null

        $reportsArgument = ($coverageFiles | ForEach-Object { $_.FullName }) -join ";"
        $reportTypes = "HtmlInline_AzurePipelines;Cobertura;TextSummary"

        & dotnet tool run reportgenerator "-reports:$reportsArgument" "-targetdir:$coverageReportDir" "-reporttypes:$reportTypes"
        if ($LASTEXITCODE -ne 0) {
            $executionIssues += "Coverage report generation failed."
        }

        $summaryPath = Join-Path -Path $coverageReportDir -ChildPath "Summary.txt"
        if (Test-Path -Path $summaryPath) {
            Write-Host ">> Coverage summary:"
            Get-Content -Path $summaryPath
        }
    }
    else {
        $executionIssues += "Coverage was requested but no coverage.cobertura.xml files were found."
    }
}

if ($AllowKnownFailures) {
    $knownFailures = @()
    if (Test-Path -Path $knownFailuresPath) {
        $knownFailures = @(Get-Content -Path $knownFailuresPath |
            ForEach-Object { $_.Trim() } |
            Where-Object { $_ -and -not $_.StartsWith("#") })
    }

    $knownFailuresInScope = @($knownFailures | Where-Object { $_ -in $executedTests })
    $unexpectedFailures = @($failedTests | Where-Object { $_ -notin $knownFailures })
    $resolvedFailures = @($knownFailuresInScope | Where-Object { $_ -notin $failedTests })

    if ($failedTests.Count -gt 0) {
        Write-Host ">> Failed tests (allow-list mode):"
        $failedTests | ForEach-Object { Write-Host " - $_" }
    }

    if ($resolvedFailures.Count -gt 0) {
        Write-Host ">> Known failures that are now passing:"
        $resolvedFailures | ForEach-Object { Write-Host " + $_" }
    }

    if ($executionIssues.Count -gt 0 -or $unexpectedFailures.Count -gt 0) {
        if ($unexpectedFailures.Count -gt 0) {
            Write-Host "!! Unexpected failing tests:"
            $unexpectedFailures | ForEach-Object { Write-Host " x $_" }
        }

        if ($executionIssues.Count -gt 0) {
            Write-Host "!! Execution issues:"
            $executionIssues | ForEach-Object { Write-Host " x $_" }
        }

        exit 1
    }

    Write-Host ">> Test execution completed with only known failures."
    exit 0
}

if ($failedTests.Count -gt 0 -or $executionIssues.Count -gt 0) {
    if ($failedTests.Count -gt 0) {
        Write-Host "!! Failing tests:"
        $failedTests | ForEach-Object { Write-Host " x $_" }
    }

    if ($executionIssues.Count -gt 0) {
        Write-Host "!! Execution issues:"
        $executionIssues | ForEach-Object { Write-Host " x $_" }
    }

    exit 1
}

Write-Host ">> All selected test suites passed."
