#Requires -Version 5.1
<#
.SYNOPSIS
    Test DuplicateTracker and write the downloadable zip archives into dist/.
#>
[CmdletBinding()]
param(
    [switch]$RequireArm64
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

function Invoke-DotNet {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

[xml]$project = Get-Content -LiteralPath (Join-Path $root 'DuplicateTracker.csproj')
$version = @($project.Project.PropertyGroup.Version) | Where-Object { $_ } | Select-Object -First 1
if (-not $version) {
    throw 'Version was not found in DuplicateTracker.csproj.'
}

Invoke-DotNet -Arguments @('test', '-c', 'Release', '--nologo')

$dist = Join-Path $root 'dist'
if (Test-Path $dist) {
    Remove-Item $dist -Recurse -Force
}

New-Item -ItemType Directory -Path $dist | Out-Null

$rids = @('win-x64', 'win-arm64')
$written = @()

foreach ($rid in $rids) {
    $publish = Join-Path $dist "publish-$rid"
    Invoke-DotNet -Arguments @(
        'publish', 'DuplicateTracker.csproj', '-c', 'Release', '-r', $rid, '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true',
        '-p:DebugType=none',
        '-p:DebugSymbols=false',
        '--output', $publish
    )

    if (-not (Test-Path (Join-Path $publish 'DuplicateTracker.exe'))) {
        if ($rid -eq 'win-arm64' -and -not $RequireArm64) {
            Write-Warning 'win-arm64 publish did not produce DuplicateTracker.exe. Continuing with the archives that succeeded.'
            if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
            continue
        }

        throw "Publish for $rid did not produce DuplicateTracker.exe."
    }

    $stageName = "DuplicateTracker-$version-$rid"
    $stage = Join-Path $dist $stageName
    New-Item -ItemType Directory -Path $stage | Out-Null
    Copy-Item (Join-Path $publish '*') $stage
    Copy-Item (Join-Path $root 'README.md') $stage
    Copy-Item (Join-Path $root 'LICENSE') $stage
    Copy-Item (Join-Path $root 'Scan-Duplicates.ps1') $stage
    @(
        '@echo off',
        'setlocal',
        '"%~dp0DuplicateTracker.exe" %*'
    ) | Set-Content -LiteralPath (Join-Path $stage 'DuplicateTracker.cmd') -Encoding ascii

    $zip = Join-Path $dist "$stageName.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal
    Remove-Item $publish -Recurse -Force
    Remove-Item $stage -Recurse -Force
    $written += $zip
    Write-Host "Wrote $zip"
}

if ($written.Count -eq 0) {
    throw 'No archives were written.'
}

$sums = foreach ($zip in $written) {
    $hash = Get-FileHash -LiteralPath $zip -Algorithm SHA256
    '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), (Split-Path $zip -Leaf)
}

$sumsPath = Join-Path $dist 'SHA256SUMS.txt'
$sums | Set-Content -LiteralPath $sumsPath -Encoding ascii
Write-Host "Wrote $sumsPath"
Get-Item $written | Select-Object Name, Length | Format-Table -AutoSize | Out-String | Write-Host
