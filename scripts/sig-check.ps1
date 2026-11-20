Get-ChildItem 'C:\Users\badbo\Downloads\LockscreenGif*.msi' | ForEach-Object {
    $sig = Get-AuthenticodeSignature $_.FullName
    Write-Output "$($_.Name) | Size: $($_.Length) bytes ($([math]::Round($_.Length / 1MB, 2)) MB) | SigStatus: $($sig.Status)"
}

$installedExe = 'C:\Program Files (x86)\SubhamPro11\Lockscreen Gif\LockscreenGif.exe'
if (Test-Path $installedExe) {
    $exeSig = Get-AuthenticodeSignature $installedExe
    Write-Output "Installed LockscreenGif.exe SigStatus: $($exeSig.Status)"
}
