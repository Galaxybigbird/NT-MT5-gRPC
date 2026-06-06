$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $repoRoot 'MultiStratManagerRepo\MultiStratManager.cs'
$source = Get-Content -Path $sourcePath -Raw
$strategyPath = Join-Path $repoRoot 'NT Strats\OG_BaseOptStrategy_Auto.cs'
$strategySource = Get-Content -Path $strategyPath -Raw

function Get-MethodBody {
    param(
        [string]$Text,
        [string]$MethodName
    )

    $signaturePattern = '(?m)\bprivate\s+[\w<>,\.\s]+\s+' + [regex]::Escape($MethodName) + '\s*\('
    $signature = [regex]::Match($Text, $signaturePattern)
    if (-not $signature.Success) {
        throw "Unable to find method $MethodName"
    }
    $methodIndex = $signature.Index

    $braceStart = $Text.IndexOf("{", $methodIndex, [StringComparison]::Ordinal)
    if ($braceStart -lt 0) {
        throw "Unable to find opening brace for $MethodName"
    }

    $depth = 0
    for ($i = $braceStart; $i -lt $Text.Length; $i++) {
        if ($Text[$i] -eq '{') {
            $depth++
        } elseif ($Text[$i] -eq '}') {
            $depth--
            if ($depth -eq 0) {
                return $Text.Substring($braceStart, $i - $braceStart + 1)
            }
        }
    }

    throw "Unable to find closing brace for $MethodName"
}

$handleBody = Get-MethodBody -Text $source -MethodName 'HandleExternalCloseExecutionFallback'
$findBody = Get-MethodBody -Text $source -MethodName 'FindExternalCloseCandidates'
$activeBody = Get-MethodBody -Text $source -MethodName 'TryPublishActiveNtTradeCloseFallback'
$syntheticBody = Get-MethodBody -Text $strategySource -MethodName 'TriggerSyntheticProtectionExitAll'
$handleExitBody = Get-MethodBody -Text $strategySource -MethodName 'HandleExitExecution'

if ($handleBody -notmatch 'resolvedRecord\s*!=\s*null\s*&&\s*!resolvedRecord\.ExternalCloseOnly') {
    throw 'Managed TradeSync records must bypass external close fallback.'
}

if ($findBody -notmatch 'exact\.Count\s*==\s*0[\s\S]*return\s+results\s*;') {
    throw 'ExternalCloseOnly matching must not broaden when a concrete resolvedTradeId has no exact match.'
}

if ($activeBody -notmatch 'exact\.Count\s*==\s*0[\s\S]*return\s+false\s*;') {
    throw 'Active NT close fallback must not remap a concrete resolvedTradeId to another active trade.'
}

if ($syntheticBody -notmatch 'allowNativeProtectionWait\s*=\s*stopExit\s*&&') {
    throw 'Synthetic target touches must submit explicit exits instead of waiting on native targets.'
}

if ($handleExitBody -notmatch 'ShouldTriggerExitAllAfterFullProtectiveClose') {
    throw 'Full target/stop fills on one managed leg must trigger exit-all fan-out for the remaining NT position.'
}

Write-Host 'NT close fallback regression guards passed.'
