<#
.SYNOPSIS
    XAML Resource Regression Guard for LockscreenGif.
.DESCRIPTION
    Validates:
    1. App.xaml merges '/Styles/InstrumentTheme.xaml' LAST in MergedDictionaries.
    2. Every StaticResource and ThemeResource in user XAML resolves to a valid dictionary key or known WinUI token.
    3. Audits duplicate keys across merged dictionaries.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$lockscreenProj = Join-Path $repoRoot "LockScreenGif"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " XAML RESOURCE REGRESSION GUARD                           " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Guard: Check App.xaml Merged Dictionaries Order
Write-Host "`n[1/3] Verifying App.xaml dictionary merge order..." -ForegroundColor Yellow
$appXamlPath = Join-Path $lockscreenProj "App.xaml"
if (-not (Test-Path $appXamlPath)) {
    throw "App.xaml not found at $appXamlPath"
}
$appXamlContent = Get-Content $appXamlPath -Raw
$mergedMatches = [regex]::Matches($appXamlContent, '<ResourceDictionary\s+Source="([^"]+)"\s*/>')
if ($mergedMatches.Count -eq 0) {
    throw "No merged dictionaries found in App.xaml!"
}
$lastMerged = $mergedMatches[$mergedMatches.Count - 1].Groups[1].Value
if ($lastMerged -notlike "*InstrumentTheme.xaml*") {
    Write-Error "FAIL: App.xaml does not merge InstrumentTheme.xaml last! Last merged is: $lastMerged"
    exit 1
}
Write-Host "PASS: App.xaml merges InstrumentTheme.xaml last ($lastMerged)." -ForegroundColor Green

# 2. Extract Defined Keys Across All Dictionaries
Write-Host "`n[2/3] Extracting defined resource keys across dictionaries..." -ForegroundColor Yellow
$dictFiles = @(
    (Join-Path $lockscreenProj "Styles\FontSizes.xaml"),
    (Join-Path $lockscreenProj "Styles\Thickness.xaml"),
    (Join-Path $lockscreenProj "Styles\TextBlock.xaml"),
    (Join-Path $lockscreenProj "Styles\InstrumentTheme.xaml"),
    (Join-Path $lockscreenProj "CustomControls\VideoTimeline.xaml")
)

$definedKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$keyOrigins = @{}

foreach ($df in $dictFiles) {
    if (Test-Path $df) {
        $c = Get-Content $df -Raw
        $kMatches = [regex]::Matches($c, 'x:Key="([^"]+)"')
        foreach ($km in $kMatches) {
            $key = $km.Groups[1].Value
            [void]$definedKeys.Add($key)
            if (-not $keyOrigins.ContainsKey($key)) {
                $keyOrigins[$key] = @()
            }
            $keyOrigins[$key] += (Split-Path -Leaf $df)
        }
    }
}
Write-Host "  Found $($definedKeys.Count) distinct resource keys." -ForegroundColor Green

# Audit duplicates
$duplicateCount = 0
foreach ($entry in $keyOrigins.GetEnumerator()) {
    $uniqueFiles = $entry.Value | Sort-Object -Unique
    if ($uniqueFiles.Count -gt 1) {
        $duplicateCount++
    }
}
Write-Host "  Audited $($duplicateCount) keys defined across multiple files." -ForegroundColor Gray

# 3. Guard: Verify All XAML Resource References Resolve
Write-Host "`n[3/3] Scanning XAML views for orphan StaticResource/ThemeResource references..." -ForegroundColor Yellow
$xamlFiles = Get-ChildItem -Path $lockscreenProj -Recurse -Filter *.xaml | Where-Object {
    $_.FullName -notmatch "\\(obj|bin|PublishProfiles)\\"
}

# Known standard WinUI 3 / Windows App SDK control contract resources
$knownSystemPrefixes = @(
    "System", "NavigationView", "ContentDialog", "Button", "ToggleSwitch",
    "Slider", "ProgressBar", "InfoBar", "ScrollBar", "ComboBox",
    "WindowCaption", "TextControl", "TextFill", "CardBackground", "SubtleFillColor",
    "SolidBackgroundFillColor", "LayerOnMicaBaseAltFillColor", "ControlFillColor",
    "ControlElevation", "FocusVisual", "DefaultToolTip"
)

$orphanErrors = @()

foreach ($xf in $xamlFiles) {
    $content = Get-Content $xf.FullName -Raw
    
    # Also extract any locally-scoped keys in this file
    $localKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $localMatches = [regex]::Matches($content, 'x:Key="([^"]+)"')
    foreach ($lm in $localMatches) {
        [void]$localKeys.Add($lm.Groups[1].Value)
    }

    $refMatches = [regex]::Matches($content, '\{(?:StaticResource|ThemeResource)\s+([^}\s]+)\}')
    foreach ($rm in $refMatches) {
        $refKey = $rm.Groups[1].Value
        
        # Check if defined in project dictionaries or local file
        if ($definedKeys.Contains($refKey) -or $localKeys.Contains($refKey)) {
            continue
        }

        # Check if matches known WinUI system token prefix
        $isSystem = $false
        foreach ($prefix in $knownSystemPrefixes) {
            if ($refKey.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                $isSystem = $true
                break
            }
        }
        if ($isSystem) {
            continue
        }

        $orphanErrors += "Orphan resource reference '{$refKey}' in $($xf.FullName.Replace($repoRoot, ''))"
    }
}

if ($orphanErrors.Count -gt 0) {
    Write-Host "FAIL: Found $($orphanErrors.Count) orphan resource reference(s):" -ForegroundColor Red
    foreach ($err in $orphanErrors) {
        Write-Host "  - $err" -ForegroundColor Red
    }
    exit 1
}

Write-Host "PASS: All XAML resource references successfully resolved." -ForegroundColor Green
Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " XAML REGRESSION GUARD: ALL CHECKS PASSED (100%)          " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
exit 0
