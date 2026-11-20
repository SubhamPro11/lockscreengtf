$sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$backupDir = 'C:\Users\badbo\AppData\Local\Temp\LockscreenGif_QA_Backup'
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null

Write-Host "=== BACKING UP LOCK SCREEN CACHE ==="
$cachePath = "C:\ProgramData\Microsoft\Windows\SystemData\$sid\ReadOnly"
if (Test-Path $cachePath) {
    Copy-Item -Path $cachePath -Destination "$backupDir\ReadOnlyCache" -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Backed up cache to $backupDir\ReadOnlyCache"
} else {
    Write-Host "No existing ReadOnly cache directory found at $cachePath"
}

Write-Host "=== BACKING UP DESKTOP REGISTRY ==="
reg export "HKCU\Control Panel\Desktop" "$backupDir\Desktop_Registry.reg" /y | Out-Null
Write-Host "Backed up HKCU\Control Panel\Desktop to $backupDir\Desktop_Registry.reg"

Write-Host "=== BACKING UP FEATURE OVERRIDES REGISTRY ==="
reg export "HKLM\SYSTEM\CurrentControlSet\Control\FeatureManagement\Overrides" "$backupDir\FeatureOverrides.reg" /y 2>$null
Write-Host "Feature overrides export attempted."

Write-Host "=== BACKING UP APP SETTINGS ==="
$appData = "$env:LOCALAPPDATA\LockscreenGif"
if (Test-Path $appData) {
    Copy-Item -Path $appData -Destination "$backupDir\AppData" -Recurse -Force
    Write-Host "Backed up $appData to $backupDir\AppData"
}

Write-Host "Backup completed at $backupDir."
Get-ChildItem -Path $backupDir | Select-Object Name, Length, LastWriteTime
