<#
.SYNOPSIS
Deploy NinjaScript strategy sources and shared strategy helper files into NinjaTrader 8 strategy folders.
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
function Test-PathSafe { param([string]$Path) try { Test-Path $Path } catch { $false } }

function Write-Success { param($Message) Write-Host "[SUCCESS] $Message" -ForegroundColor Green }
function Write-WarningLine { param($Message) Write-Host "[WARNING] $Message" -ForegroundColor Yellow }
function Write-ErrorLine { param($Message) Write-Host "[ERROR] $Message" -ForegroundColor Red }
function Write-Info { param($Message) Write-Host "[INFO] $Message" -ForegroundColor Cyan }
function Write-DebugLine { param($Message) if ($Verbose) { Write-Host "[DEBUG] $Message" -ForegroundColor Gray } }

Write-Host '================ NinjaScript Strategy Deployment ================' -ForegroundColor Cyan
Write-Host 'Deploying top-level strategy .cs files and SharedHelperFiles into NinjaTrader 8' -ForegroundColor Yellow
Write-Host ("Start: {0:yyyy-MM-dd HH:mm:ss}" -f (Get-Date)) -ForegroundColor Gray

if (-not (Test-Path $SourceRoot)) {
    Write-ErrorLine "Source directory not found: $SourceRoot"
    exit 1
}

Write-Info "Source directory: $SourceRoot"
Write-Info "Helper directory: $helperSourceRoot"

function Get-NinjaTraderInstallations {
    $candidateRoots = New-Object 'System.Collections.Generic.List[string]'
    $profileRoots = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)

    $currentProfile = [Environment]::GetFolderPath('UserProfile')
    if (-not [string]::IsNullOrWhiteSpace($currentProfile)) {
        [void]$profileRoots.Add($currentProfile.TrimEnd('\'))
    }

    if (Test-PathSafe 'C:\Users') {
        Get-ChildItem -Path 'C:\Users' -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -notin @('All Users', 'Default', 'Default User', 'Public', 'WDAGUtilityAccount') } |
            ForEach-Object {
                [void]$profileRoots.Add($_.FullName.TrimEnd('\'))
            }
    }

    foreach ($profileRoot in $profileRoots) {
        foreach ($candidate in @(
            (Join-Path $profileRoot 'Documents\NinjaTrader 8'),
            (Join-Path $profileRoot 'OneDrive\Documents\NinjaTrader 8'),
            (Join-Path $profileRoot 'OneDrive\Old video editing files\NinjaTrader 8'),
            (Join-Path $profileRoot 'OneDrive\Desktop\OneDrive\Old video editing files\NinjaTrader 8'),
            (Join-Path $profileRoot 'Desktop\NinjaTrader 8')
        )) {
            if (-not [string]::IsNullOrWhiteSpace($candidate)) {
                $candidateRoots.Add($candidate)
            }
        }

        foreach ($scanRoot in @(
            (Join-Path $profileRoot 'Documents'),
            (Join-Path $profileRoot 'OneDrive'),
            (Join-Path $profileRoot 'Desktop'),
            (Join-Path $profileRoot 'OneDrive\Desktop')
        )) {
            if (-not (Test-PathSafe $scanRoot)) {
                continue
            }

            try {
                Get-ChildItem -Path $scanRoot -Directory -Recurse -Depth 4 -ErrorAction SilentlyContinue |
                    Where-Object { $_.Name -eq 'NinjaTrader 8' } |
                    ForEach-Object {
                        $candidateRoots.Add($_.FullName)
                    }
            }
            catch {
                Write-DebugLine "Skipping scan root ${scanRoot}: $($_.Exception.Message)"
            }
        }
    }

    $seenPaths = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $installations = New-Object 'System.Collections.Generic.List[object]'

    foreach ($candidateRoot in $candidateRoots) {
        if ([string]::IsNullOrWhiteSpace($candidateRoot)) {
            continue
        }

        $normalizedRoot = $candidateRoot.TrimEnd('\')
        if (-not (Test-PathSafe $normalizedRoot)) {
            continue
        }

        if (-not $seenPaths.Add($normalizedRoot)) {
            continue
        }

        $customPath = Join-Path $normalizedRoot 'bin\Custom'
        $strategyPath = Join-Path $customPath 'Strategies'
        $score = 0

        if (Test-PathSafe $strategyPath) {
            $score += 4
        }

        if (Test-PathSafe $customPath) {
            $score += 2
        }

        if ($normalizedRoot -like '*\\Documents\\NinjaTrader 8') {
            $score += 1
        }

        $installations.Add([PSCustomObject]@{
            Path = $normalizedRoot
            Name = Split-Path $normalizedRoot -Leaf
            CustomPath = $customPath
            StrategiesPath = $strategyPath
            Exists = Test-PathSafe $strategyPath
            CustomExists = Test-PathSafe $customPath
            Score = $score
        })
    }

    return $installations | Sort-Object -Property @{ Expression = 'Score'; Descending = $true }, @{ Expression = 'Path'; Descending = $false }
}

$installations = Get-NinjaTraderInstallations
if ($installations.Count -eq 0) {
    Write-ErrorLine 'No NinjaTrader 8 roots were detected under C:\Users.'
    Write-Info 'Checked common locations under each Windows user profile: Documents, OneDrive, Desktop, and OneDrive\\Desktop.'
    exit 1
}

if ($ListInstallations) {
    Write-Info 'Detected NinjaTrader 8 installations:'
    for ($i = 0; $i -lt $installations.Count; $i++) {
        $inst = $installations[$i]
        $status = if ($inst.Exists) { '[OK]' } elseif ($inst.CustomExists) { '[CREATE STRATEGIES]' } else { '[CREATE CUSTOM]' }
        $color = if ($inst.Exists) { 'Green' } elseif ($inst.CustomExists) { 'Yellow' } else { 'DarkYellow' }
        Write-Host "  [$i] $status $($inst.Path)" -ForegroundColor $color
    }
    exit 0
}

if ($TargetRoot) {
    $normalizedTargetRoot = $TargetRoot.TrimEnd('\')
    $installations = @($installations | Where-Object { $_.Path -eq $normalizedTargetRoot })

    if ($installations.Count -eq 0) {
        if (-not (Test-Path $normalizedTargetRoot)) {
            Write-ErrorLine "Specified target root not found or not valid: $TargetRoot"
            exit 1
        }

        $customPath = Join-Path $normalizedTargetRoot 'bin\Custom'
        $strategyPath = Join-Path $customPath 'Strategies'
        $installations = @([PSCustomObject]@{
            Path = $normalizedTargetRoot
            Name = Split-Path $normalizedTargetRoot -Leaf
            CustomPath = $customPath
            StrategiesPath = $strategyPath
            Exists = Test-PathSafe $strategyPath
            CustomExists = Test-PathSafe $customPath
            Score = 999
        })
    }
}

Write-Info 'Target installation(s):'
$installations | ForEach-Object {
    $status = if ($_.Exists) { '[OK]' } elseif ($_.CustomExists) { '[CREATE STRATEGIES]' } else { '[CREATE CUSTOM]' }
    $color = if ($_.Exists) { 'Green' } elseif ($_.CustomExists) { 'Yellow' } else { 'DarkYellow' }
    Write-Host "  $status $($_.Path)" -ForegroundColor $color
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

    if (-not (Test-Path $Path)) {
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
    if (-not (Test-Path $destinationDir)) {
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
    if (-not $installation.CustomExists) {
        if ($DryRun) {
            Write-WarningLine "DRYRUN: Custom directory missing at $($installation.CustomPath)"
        }
        else {
            Write-Info "Custom directory missing; creating $($installation.CustomPath)"
            New-Item -ItemType Directory -Path $installation.CustomPath -Force | Out-Null
            $installation.CustomExists = $true
        }
    }

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
        Write-WarningLine "Skipping invalid installation: $($installation.Path)"
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


