<#
.SYNOPSIS
Deploy only NinjaScript C# source files to NinjaTrader installations for quick iteration.

.DESCRIPTION
This script copies only the main NinjaScript addon source files (.cs) from the repository 
to NinjaTrader installations, preserving existing DLL dependencies and External folders.
Perfect for quick development iteration without rebuilding gRPC dependencies.

.PARAMETER SourceRoot
Source directory containing the NinjaScript files. Defaults to MultiStratManagerRepo.

.PARAMETER TargetRoot
Specific NinjaTrader installation to target. If not specified, auto-detects all installations.

.PARAMETER DryRun
Preview mode - shows what would be copied without actually copying files.

.PARAMETER Verbose
Enable detailed logging output.

.PARAMETER Force
Overwrite existing files without prompting.

.PARAMETER ListInstallations
Only list detected NinjaTrader installations and exit.

.EXAMPLE
.\deploy-sources-only.ps1
Deploy sources to all detected NinjaTrader installations.

.EXAMPLE
.\deploy-sources-only.ps1 -DryRun -Verbose
Preview what would be copied with detailed output.

.EXAMPLE
.\deploy-sources-only.ps1 -TargetRoot "C:\Users\User\Documents\NinjaTrader 8"
Deploy to specific NinjaTrader installation only.
#>

param(
    [string]$SourceRoot = "C:\Documents\Dev\OfficialFuturesHedgebotv2\MultiStratManagerRepo",
    [string]$TargetRoot,
    [switch]$DryRun,
    [switch]$Verbose,
    [switch]$Force,
    [switch]$ListInstallations
)

$ErrorActionPreference = 'Stop'

# Color-coded output functions
function Write-Success { param($Message) Write-Host "[SUCCESS] $Message" -ForegroundColor Green }
function Write-Warning { param($Message) Write-Host "[WARNING] $Message" -ForegroundColor Yellow }
function Write-Error { param($Message) Write-Host "[ERROR] $Message" -ForegroundColor Red }
function Write-Info { param($Message) Write-Host "[INFO] $Message" -ForegroundColor Cyan }
function Write-Debug { param($Message) if ($Verbose) { Write-Host "[DEBUG] $Message" -ForegroundColor Gray } }

Write-Host "================ NinjaScript Sources Deployment ================" -ForegroundColor Cyan
Write-Host "Quick deployment of C# source files for development iteration" -ForegroundColor Yellow
Write-Host ("Start: {0:yyyy-MM-dd HH:mm:ss}" -f (Get-Date)) -ForegroundColor Gray

# Validate source directory
if (!(Test-Path $SourceRoot)) {
    Write-Error "Source directory not found: $SourceRoot"
    exit 1
}

Write-Info "Source directory: $SourceRoot"

# Single target NinjaTrader installation
function Get-NinjaTraderInstallations {
    $installations = @()

    # SINGLE TARGET PATH ONLY
    $targetPath = 'C:\Users\marth\OneDrive\Desktop\OneDrive\Old video editing files\NinjaTrader 8'

    if (Test-Path $targetPath) {
        $installations += [PSCustomObject]@{
            Path = $targetPath
            Name = Split-Path $targetPath -Leaf
            BinCustom = Join-Path $targetPath 'bin\Custom'
            AddonPath = Join-Path $targetPath 'bin\Custom\AddOns\MultiStratManager'
            Exists = Test-Path (Join-Path $targetPath 'bin\Custom')
        }
    }

    return $installations
}

$installations = Get-NinjaTraderInstallations

if ($installations.Count -eq 0) {
    Write-Error "Target NinjaTrader 8 installation not found!"
    Write-Info "Expected location: C:\Users\marth\OneDrive\Desktop\OneDrive\Old video editing files\NinjaTrader 8"
    exit 1
}

# List installations if requested
if ($ListInstallations) {
    Write-Info "Detected NinjaTrader 8 installations:"
    for ($i = 0; $i -lt $installations.Count; $i++) {
        $inst = $installations[$i]
        $status = if ($inst.Exists) { "[OK]" } else { "[MISSING]" }
        Write-Host "  [$i] $status $($inst.Path)" -ForegroundColor $(if ($inst.Exists) { "Green" } else { "Red" })
    }
    exit 0
}

# Filter installations based on target
if ($TargetRoot) {
    $installations = $installations | Where-Object { $_.Path -eq $TargetRoot }
    if ($installations.Count -eq 0) {
        Write-Error "Specified target root not found or not a valid NinjaTrader installation: $TargetRoot"
        exit 1
    }
}

Write-Info "Target installation:"
$installations | ForEach-Object {
    $status = if ($_.Exists) { "[OK]" } else { "[MISSING]" }
    Write-Host "  $status $($_.Path)" -ForegroundColor $(if ($_.Exists) { "Green" } else { "Red" })
}

# Discover .cs files directly under the source root
Write-Debug "Scanning source root for .cs files: $SourceRoot"
$foundFiles = Get-ChildItem -Path $SourceRoot -Filter '*.cs' -File | Select-Object Name, FullName, Length, LastWriteTime

if ($foundFiles.Count -eq 0) {
    Write-Error "No .cs files found in the source root: $SourceRoot"
    exit 1
}

Write-Info "Source files to deploy ($($foundFiles.Count)):"
$foundFiles | ForEach-Object {
    Write-Host "  [FILE] $($_.Name) ($($_.Length) bytes, modified: $($_.LastWriteTime.ToString('yyyy-MM-dd HH:mm')))" -ForegroundColor White
}

# Deployment summary
$deploymentResults = @()

foreach ($installation in $installations) {
    if (!$installation.Exists) {
        Write-Warning "Skipping invalid installation: $($installation.Path)"
        continue
    }
    
    Write-Info "Processing: $($installation.Path)"
    
    # Ensure addon directory exists
    if (!(Test-Path $installation.AddonPath)) {
        if ($DryRun) {
            Write-Debug "DRYRUN: Would create directory: $($installation.AddonPath)"
        } else {
            try {
                New-Item -ItemType Directory -Path $installation.AddonPath -Force | Out-Null
                Write-Success "Created addon directory: $($installation.AddonPath)"
            } catch {
                Write-Error "Failed to create addon directory: $($_.Exception.Message)"
                continue
            }
        }
    }
    
    # Copy each source file
    $copiedFiles = 0
    $skippedFiles = 0
    $errorFiles = 0
    
    foreach ($sourceFile in $foundFiles) {
        $targetPath = Join-Path $installation.AddonPath $sourceFile.Name
        
        try {
            # Check if target exists and compare
            $shouldCopy = $true
            if (Test-Path $targetPath) {
                $targetFile = Get-Item $targetPath
                if ($targetFile.LastWriteTime -ge $sourceFile.LastWriteTime -and !$Force) {
                    Write-Debug "Target is newer or same age, skipping: $($sourceFile.Name)"
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
    
    # Preserve External folder (don't touch DLLs)
    $externalPath = Join-Path $installation.AddonPath 'External'
    if (Test-Path $externalPath) {
        $dllCount = (Get-ChildItem -Path $externalPath -Filter '*.dll' -ErrorAction SilentlyContinue).Count
        Write-Info "Preserved External folder with $dllCount DLL files"
    }
    
    $deploymentResults += [PSCustomObject]@{
        Installation = $installation.Path
        Copied = $copiedFiles
        Skipped = $skippedFiles
        Errors = $errorFiles
        Status = if ($errorFiles -eq 0) { "SUCCESS" } else { "PARTIAL" }
    }
}

# Summary
Write-Host "`n================ Deployment Summary ================" -ForegroundColor Cyan

$deploymentResults | ForEach-Object {
    $statusColor = switch ($_.Status) {
        "SUCCESS" { "Green" }
        "PARTIAL" { "Yellow" }
        default { "Red" }
    }
    Write-Host "$($_.Status): $($_.Installation)" -ForegroundColor $statusColor
    Write-Host "  [STATS] Copied: $($_.Copied), Skipped: $($_.Skipped), Errors: $($_.Errors)" -ForegroundColor Gray
}

$totalCopied = ($deploymentResults | Measure-Object -Property Copied -Sum).Sum
$totalErrors = ($deploymentResults | Measure-Object -Property Errors -Sum).Sum

if ($DryRun) {
    Write-Info "DRY RUN completed - no files were actually copied"
} elseif ($totalErrors -eq 0) {
    Write-Success "Deployment completed successfully! Copied $totalCopied files total."
} else {
    Write-Warning "Deployment completed with $totalErrors errors. Copied $totalCopied files total."
}

Write-Host "`n[NEXT STEPS]:" -ForegroundColor Yellow
Write-Host "1. Open NinjaTrader" -ForegroundColor White
Write-Host "2. Press F5 to recompile NinjaScript" -ForegroundColor White
Write-Host "3. Check Output tab for compilation errors" -ForegroundColor White
Write-Host "4. Open addon from Tools → Multi-Strategy Manager" -ForegroundColor White

Write-Host ("End: {0:yyyy-MM-dd HH:mm:ss}" -f (Get-Date)) -ForegroundColor Gray
Write-Success "Sources-only deployment complete!"
