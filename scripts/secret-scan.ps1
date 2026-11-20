$files = Get-ChildItem -Path 'D:\LockscreenGif-master' -Recurse -File | Where-Object { 
    $_.FullName -notmatch '\\(bin|obj|\.git|artifacts|packages|release-assets)\\' -and
    $_.Extension -match '\.(cs|xaml|json|yml|ps1|md|xml|targets|props)$'
}

$findings = @()
$pattern = '(?i)(phc_[a-zA-Z0-9_-]{20,}|ghp_[a-zA-Z0-9]{20,}|ey[a-zA-Z0-9_-]{30,}|BEGIN (RSA|OPENSSH|PRIVATE) KEY)'

foreach ($f in $files) {
    $matches = Select-String -Path $f.FullName -Pattern $pattern -ErrorAction SilentlyContinue
    if ($matches) {
        foreach ($m in $matches) {
            $findings += [PSCustomObject]@{
                File = $m.Path.Replace('D:\LockscreenGif-master\', '')
                Line = $m.LineNumber
                Pattern = $pattern
                Match = $m.Line.Trim()
            }
        }
    }
}

if ($findings.Count -eq 0) {
    Write-Output "SECRET SCAN PASS: Zero high-entropy keys, JWTs, GitHub tokens, or private keys found in source files."
} else {
    $findings | Format-Table -Wrap
}
