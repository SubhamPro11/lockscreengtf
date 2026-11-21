param(
    [Parameter(Mandatory)][ValidateRange(1, 16776959)][long]$RunNumber,
    [Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$RunAttempt,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$Commit,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
# Epoch 4: v4.26.11 release line
$version = "4.26.11"
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
