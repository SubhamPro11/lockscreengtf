param(
    [Parameter(Mandatory)][ValidateRange(1, 15007514)][long]$RunNumber,
    [Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$RunAttempt,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$Commit,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
# MSI limits major/minor to 255 and build to 65535. Epoch 4.27+ auto-increments
# on every GitHub Actions push and sorts strictly after 4.26.11.
$minor = 27 + [long][Math]::Floor($RunNumber / 65535)
$patch = $RunNumber % 65535
$version = "4.$minor.$patch"
$identity = [ordered]@{
    version = $version
    informational_version = "$version-ci+$($Commit.ToLowerInvariant()).attempt.$RunAttempt"
    tag = "build-$RunNumber"
}
if ($OutputPath) {
    $identity.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" } |
        Add-Content -LiteralPath $OutputPath -Encoding utf8
}
[pscustomobject]$identity
