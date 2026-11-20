$xamlFiles = Get-ChildItem -Path "D:\LockscreenGif-master\LockScreenGif" -Recurse -Filter "*.xaml"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " PHASE 8 QA: ACCESSIBILITY & AUTOMATION PROPERTIES CHECK   " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$missingLabels = [System.Collections.Generic.List[string]]::new()
$totalControls = 0
$labeledControls = 0

foreach ($file in $xamlFiles) {
    [xml]$xml = Get-Content -LiteralPath $file.FullName
    $nodes = $xml.SelectNodes("//*")
    foreach ($node in $nodes) {
        $name = $node.LocalName
        if ($name -in @("Button", "ComboBox", "ToggleSwitch", "Slider", "TextBox", "CheckBox", "HyperlinkButton")) {
            $totalControls++
            $hasName = $node.HasAttribute("AutomationProperties.Name")
            $hasContent = $node.HasAttribute("Content")
            $hasHeader = $node.HasAttribute("Header")
            $hasPlaceholder = $node.HasAttribute("PlaceholderText")
            $hasText = ($node.ChildNodes | Where-Object { $_.LocalName -eq "TextBlock" -and $_.InnerText.Trim().Length -gt 0 }).Count -gt 0

            if ($hasName -or $hasContent -or $hasHeader -or $hasPlaceholder -or $hasText) {
                $labeledControls++
            } else {
                $ctrlId = if ($node.HasAttribute("x:Name")) { $node.GetAttribute("x:Name") } else { "<anonymous>" }
                $missingLabels.Add("$($file.Name): <$name x:Name='$ctrlId'> lacks accessible name")
            }
        }
    }
}

Write-Host "Evaluated $totalControls interactive controls across $($xamlFiles.Count) XAML views."
Write-Host "Accessible / Labeled Controls: $labeledControls / $totalControls ($([math]::Round(($labeledControls/$totalControls)*100, 1))%)"

if ($missingLabels.Count -gt 0) {
    Write-Host "`nControls needing accessible label check ($($missingLabels.Count)):" -ForegroundColor Yellow
    $missingLabels | Select-Object -First 15 | ForEach-Object { Write-Host " - $_" }
} else {
    Write-Host "PASS: 100% of interactive controls possess accessible names or text contents!" -ForegroundColor Green
}
