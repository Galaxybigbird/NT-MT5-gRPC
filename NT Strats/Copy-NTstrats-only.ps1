<#
.SYNOPSIS
Deploy NinjaScript Strategy source files from the repo root into the NinjaTrader Strategies folder.

.DESCRIPTION
This script mirrors `Copy-NTsources-only.ps1` but targets the strategy .cs files located
directly under the `NT Strats` folder and deploys them into the NinjaTrader `bin\Custom\Strategies`
directory for rapid manual/auto strategy iteration.

.PARAMETER SourceRoot
Source directory containing the strategy .cs files. Defaults to the repository's `NT Strats` folder.

.PARAMETER TargetRoot
Specific NinjaTrader installation to target. If not specified, uses the standard OneDrive location.

.PARAMETER DryRun
Preview mode - shows what would be copied without actually copying files.

.PARAMETER Verbose
Enable detailed logging output.

.PARAMETER Force
Overwrite existing files without prompting.

.PARAMETER ListInstallations
Only list detected NinjaTrader installations and exit.
#>

param(
    [string]$SourceRoot,
    [string]$TargetRoot,
    [switch]$DryRun,
    [switch]$Verbose,
    [switch]$Force,
    [switch]$ListInstallations
)

$ErrorActionPreference = 'Stop'

if (-not $SourceRoot) {
    if ($PSScriptRoot) {
        $SourceRoot = (Resolve-Path -Path $PSScriptRoot).Path
    } else {
        $SourceRoot = "C:\\Documents\\Dev\\OfficialFuturesHedgebotv2\\NT Strats"
    }
}

function Write-Success { param($Message) Write-Host "[SUCCESS] $Message" -ForegroundColor Green }
function Write-Warning { param($Message) Write-Host "[WARNING] $Message" -ForegroundColor Yellow }
function Write-Error { param($Message) Write-Host "[ERROR] $Message" -ForegroundColor Red }
function Write-Info { param($Message) Write-Host "[INFO] $Message" -ForegroundColor Cyan }
function Write-Debug { param($Message) if ($Verbose) { Write-Host "[DEBUG] $Message" -ForegroundColor Gray } }

Write-Host "================ NinjaScript Strategy Deployment ================" -ForegroundColor Cyan
Write-Host "Deploying top-level strategy .cs files into NinjaTrader 8" -ForegroundColor Yellow
Write-Host ("Start: {0:yyyy-MM-dd HH:mm:ss}" -f (Get-Date)) -ForegroundColor Gray

if (!(Test-Path $SourceRoot)) {
    Write-Error "Source directory not found: $SourceRoot"
    exit 1
}

Write-Info "Source directory: $SourceRoot"

function Get-NinjaTraderInstallations {
    $installations = @()
    $targetPath = 'C:\\Users\\marth\\OneDrive\\Desktop\\OneDrive\\Old video editing files\\NinjaTrader 8'

    if (Test-Path $targetPath) {
        $strategyPath = Join-Path $targetPath 'bin\\Custom\\Strategies'
        $installations += [PSCustomObject]@{
            Path = $targetPath
            Name = Split-Path $targetPath -Leaf
            StrategiesPath = $strategyPath
            Exists = Test-Path $strategyPath
        }
    }

    return $installations
}

$installations = Get-NinjaTraderInstallations

if ($installations.Count -eq 0) {
    Write-Error "Target NinjaTrader 8 installation not found!"
    Write-Info "Expected location: C:\\Users\\marth\\OneDrive\\Desktop\\OneDrive\\Old video editing files\\NinjaTrader 8"
    exit 1
}

if ($ListInstallations) {
    Write-Info "Detected NinjaTrader 8 installations:"
    for ($i = 0; $i -lt $installations.Count; $i++) {
        $inst = $installations[$i]
        $status = if ($inst.Exists) { '[OK]' } else { '[MISSING]' }
        Write-Host "  [$i] $status $($inst.StrategiesPath)" -ForegroundColor $(if ($inst.Exists) { 'Green' } else { 'Red' })
    }
    exit 0
}

if ($TargetRoot) {
    $installations = $installations | Where-Object { $_.Path -eq $TargetRoot }
    if ($installations.Count -eq 0) {
        Write-Error "Specified target root not found or not valid: $TargetRoot"
        exit 1
    }
}

Write-Info "Target installation(s):"
$installations | ForEach-Object {
    $status = if ($_.Exists) { '[OK]' } else { '[MISSING]' }
    Write-Host "  $status $($_.StrategiesPath)" -ForegroundColor $(if ($_.Exists) { 'Green' } else { 'Red' })
}

Write-Debug "Scanning source root for .cs files"
$foundFiles = Get-ChildItem -Path $SourceRoot -Filter '*.cs' -File | Where-Object { $_.DirectoryName -eq $SourceRoot }

if ($foundFiles.Count -eq 0) {
    Write-Error "No .cs files found directly under source root: $SourceRoot"
    exit 1
}

Write-Info "Strategy files to deploy ($($foundFiles.Count)):"
$foundFiles | ForEach-Object {
    Write-Host "  [FILE] $($_.Name) ($($_.Length) bytes, modified: $($_.LastWriteTime.ToString('yyyy-MM-dd HH:mm')))" -ForegroundColor White
}

foreach ($installation in $installations) {
    if (!$installation.Exists) {
        if ($DryRun) {
            Write-Warning "DRYRUN: Strategy directory missing at $($installation.StrategiesPath)"
        } else {
            Write-Info "Strategy directory missing; creating $($installation.StrategiesPath)"
            New-Item -ItemType Directory -Path $installation.StrategiesPath -Force | Out-Null
            $installation.Exists = $true
        }
    }

    if (!$installation.Exists) {
        Write-Warning "Skipping invalid installation: $($installation.StrategiesPath)"
        continue
    }

    Write-Info "Deploying to $($installation.StrategiesPath)"
    $copiedFiles = 0
    $skippedFiles = 0
    $errorFiles = 0

    foreach ($sourceFile in $foundFiles) {
        $targetPath = Join-Path $installation.StrategiesPath $sourceFile.Name
        try {
            $shouldCopy = $true
            if (Test-Path $targetPath) {
                $targetFile = Get-Item $targetPath
                if ($targetFile.LastWriteTime -ge $sourceFile.LastWriteTime -and !$Force) {
                    Write-Debug "Target up to date, skipping: $($sourceFile.Name)"
                    $shouldCopy = $false
                    $skippedFiles++
                }
            }

            if ($shouldCopy) {
                if ($DryRun) {
                    Write-Host "  [DRYRUN] Would copy $($sourceFile.Name)" -ForegroundColor Cyan
                } else {
                    Copy-Item -Path $sourceFile.FullName -Destination $targetPath -Force
                    Write-Success "Copied: $($sourceFile.Name)"
                    $copiedFiles++
                }
            }
        } catch {
            Write-Error "Failed to copy $($sourceFile.Name): $($_.Exception.Message)"
            $errorFiles++
        }
    }

    Write-Info "Summary for $($installation.StrategiesPath): Copied=$copiedFiles Skipped=$skippedFiles Errors=$errorFiles"
}

Write-Host ("Completed: {0:yyyy-MM-dd HH:mm:ss}" -f (Get-Date)) -ForegroundColor Gray
Write-Host "================ Deployment Finished ================" -ForegroundColor Cyan
