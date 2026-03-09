<#
.SYNOPSIS
Deploy NinjaScript strategy sources and shared strategy helper files into the NinjaTrader 8 Strategies folder.
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
    }
    else {
        $SourceRoot = 'C:\Documents\Dev\OfficialFuturesHedgebotv2\NT Strats'
    }
}

$helperSourceRoot = Join-Path $SourceRoot 'SharedHelperFiles'

function Write-Success { param($Message) Write-Host "[SUCCESS] $Message" -ForegroundColor Green }
function Write-WarningLine { param($Message) Write-Host "[WARNING] $Message" -ForegroundColor Yellow }
function Write-ErrorLine { param($Message) Write-Host "[ERROR] $Message" -ForegroundColor Red }
function Write-Info { param($Message) Write-Host "[INFO] $Message" -ForegroundColor Cyan }
function Write-DebugLine { param($Message) if ($Verbose) { Write-Host "[DEBUG] $Message" -ForegroundColor Gray } }

Write-Host '================ NinjaScript Strategy Deployment ================' -ForegroundColor Cyan
Write-Host 'Deploying top-level strategy .cs files and SharedHelperFiles into NinjaTrader 8' -ForegroundColor Yellow
Write-Host ("Start: {0:yyyy-MM-dd HH:mm:ss}" -f (Get-Date)) -ForegroundColor Gray

if (!(Test-Path $SourceRoot)) {
    Write-ErrorLine "Source directory not found: $SourceRoot"
    exit 1
}

Write-Info "Source directory: $SourceRoot"
Write-Info "Helper directory: $helperSourceRoot"

function Get-NinjaTraderInstallations {
    $installations = @()
    $targetPath = 'C:\Users\marth\OneDrive\Desktop\OneDrive\Old video editing files\NinjaTrader 8'

    if (Test-Path $targetPath) {
        $strategyPath = Join-Path $targetPath 'bin\Custom\Strategies'
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
    Write-ErrorLine 'Target NinjaTrader 8 installation not found!'
    Write-Info 'Expected location: C:\Users\marth\OneDrive\Desktop\OneDrive\Old video editing files\NinjaTrader 8'
    exit 1
}

if ($ListInstallations) {
    Write-Info 'Detected NinjaTrader 8 installations:'
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
        Write-ErrorLine "Specified target root not found or not valid: $TargetRoot"
        exit 1
    }
}

Write-Info 'Target installation(s):'
$installations | ForEach-Object {
    $status = if ($_.Exists) { '[OK]' } else { '[MISSING]' }
    Write-Host "  $status $($_.StrategiesPath)" -ForegroundColor $(if ($_.Exists) { 'Green' } else { 'Red' })
}

$strategyFiles = Get-ChildItem -Path $SourceRoot -Filter '*.cs' -File | Where-Object {
    $_.DirectoryName -eq $SourceRoot
}

$excludedHelperNames = @(
    'SharedDemaAtrTrailing.cs'
)

$helperFiles = @()
if (Test-Path $helperSourceRoot) {
    $helperFiles = Get-ChildItem -Path $helperSourceRoot -Filter '*.cs' -File -Recurse | Where-Object {
        $_.FullName -notmatch '\\Backup Code\\' -and
        $_.FullName -notmatch '\\Old strat code\\' -and
        $excludedHelperNames -notcontains $_.Name
    }
}

if ($strategyFiles.Count -eq 0 -and $helperFiles.Count -eq 0) {
    Write-ErrorLine 'No deployable strategy/helper .cs files found.'
    exit 1
}

Write-Info "Top-level strategy files to deploy ($($strategyFiles.Count)):"
$strategyFiles | ForEach-Object {
    Write-Host "  [STRAT] $($_.Name) -> Strategies\$($_.Name)" -ForegroundColor White
}

Write-Info "Shared helper files to deploy ($($helperFiles.Count)):"
$helperFiles | ForEach-Object {
    $relativePath = $_.FullName.Substring($helperSourceRoot.Length).TrimStart('\')
    Write-Host "  [HELPER] $relativePath -> Strategies\SharedHelperFiles\$relativePath" -ForegroundColor White
}

function Get-FileFingerprint {
    param(
        [string]$Path
    )

    if (!(Test-Path $Path)) {
        return $null
    }

    $item = Get-Item $Path
    $hash = (Get-FileHash -Path $Path -Algorithm SHA256).Hash
    return [PSCustomObject]@{
        Path = $item.FullName
        Length = $item.Length
        LastWriteTime = $item.LastWriteTime
        Hash = $hash
    }
}

function Copy-IfNeeded {
    param(
        [System.IO.FileInfo]$SourceFile,
        [string]$DestinationPath,
        [switch]$DryRunCopy,
        [switch]$ForceCopy
    )

    $destinationDir = Split-Path -Path $DestinationPath -Parent
    if (!(Test-Path $destinationDir)) {
        if ($DryRunCopy) {
            Write-Host "  [DRYRUN] Would create directory $destinationDir" -ForegroundColor Cyan
        }
        else {
            New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null
        }
    }

    $sourceFingerprint = Get-FileFingerprint -Path $SourceFile.FullName
    if ($null -eq $sourceFingerprint) {
        throw "Source file fingerprint could not be read: $($SourceFile.FullName)"
    }

    $targetFingerprint = Get-FileFingerprint -Path $DestinationPath
    $shouldCopy = $true
    if ($targetFingerprint -and -not $ForceCopy) {
        if ($targetFingerprint.Hash -eq $sourceFingerprint.Hash) {
            $shouldCopy = $false
        }
    }

    if (-not $shouldCopy) {
        Write-DebugLine "Up to date (hash match): $DestinationPath [$($sourceFingerprint.Hash.Substring(0, 12))]"
        return 'Skipped'
    }

    if ($DryRunCopy) {
        if ($targetFingerprint) {
            Write-Host "  [DRYRUN] Would copy to $DestinationPath (content differs)" -ForegroundColor Cyan
        }
        else {
            Write-Host "  [DRYRUN] Would copy to $DestinationPath" -ForegroundColor Cyan
        }
        return 'DryRun'
    }

    Copy-Item -Path $SourceFile.FullName -Destination $DestinationPath -Force

    $copiedFingerprint = Get-FileFingerprint -Path $DestinationPath
    if ($null -eq $copiedFingerprint -or $copiedFingerprint.Hash -ne $sourceFingerprint.Hash) {
        throw "Post-copy verification failed for $DestinationPath"
    }

    Write-Success "Copied -> $DestinationPath [$($sourceFingerprint.Hash.Substring(0, 12))]"
    return 'Copied'
}

foreach ($installation in $installations) {
    if (-not $installation.Exists) {
        if ($DryRun) {
            Write-WarningLine "DRYRUN: Strategy directory missing at $($installation.StrategiesPath)"
        }
        else {
            Write-Info "Strategy directory missing; creating $($installation.StrategiesPath)"
            New-Item -ItemType Directory -Path $installation.StrategiesPath -Force | Out-Null
            $installation.Exists = $true
        }
    }

    if (-not $installation.Exists) {
        Write-WarningLine "Skipping invalid installation: $($installation.StrategiesPath)"
        continue
    }

    Write-Info "Deploying to $($installation.StrategiesPath)"
    $copiedFiles = 0
    $skippedFiles = 0
    $errorFiles = 0

    foreach ($excludedHelperName in $excludedHelperNames) {
        $legacyTargetPath = Join-Path $installation.StrategiesPath (Join-Path 'SharedHelperFiles' $excludedHelperName)
        if (Test-Path $legacyTargetPath) {
            if ($DryRun) {
                Write-Host "  [DRYRUN] Would remove stale helper $legacyTargetPath" -ForegroundColor Cyan
            }
            else {
                Remove-Item -Path $legacyTargetPath -Force
                Write-Success "Removed stale helper -> $legacyTargetPath"
            }
        }
    }

    foreach ($strategyFile in $strategyFiles) {
        $targetPath = Join-Path $installation.StrategiesPath $strategyFile.Name
        try {
            $result = Copy-IfNeeded -SourceFile $strategyFile -DestinationPath $targetPath -DryRunCopy:$DryRun -ForceCopy:$Force
            if ($result -eq 'Copied') { $copiedFiles++ }
            elseif ($result -eq 'Skipped') { $skippedFiles++ }
        }
        catch {
            Write-ErrorLine "Failed to copy strategy $($strategyFile.Name): $($_.Exception.Message)"
            $errorFiles++
        }
    }

    foreach ($helperFile in $helperFiles) {
        $relativePath = $helperFile.FullName.Substring($helperSourceRoot.Length).TrimStart('\')
        $targetPath = Join-Path $installation.StrategiesPath (Join-Path 'SharedHelperFiles' $relativePath)
        try {
            $result = Copy-IfNeeded -SourceFile $helperFile -DestinationPath $targetPath -DryRunCopy:$DryRun -ForceCopy:$Force
            if ($result -eq 'Copied') { $copiedFiles++ }
            elseif ($result -eq 'Skipped') { $skippedFiles++ }
        }
        catch {
            Write-ErrorLine "Failed to copy helper ${relativePath}: $($_.Exception.Message)"
            $errorFiles++
        }
    }

    Write-Info "Summary for $($installation.StrategiesPath): Copied=$copiedFiles Skipped=$skippedFiles Errors=$errorFiles"
}

Write-Host ("Completed: {0:yyyy-MM-dd HH:mm:ss}" -f (Get-Date)) -ForegroundColor Gray
Write-Host '================ Deployment Finished ================' -ForegroundColor Cyan





