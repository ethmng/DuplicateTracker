#Requires -Version 5.1
<#
.SYNOPSIS
    Scan a Windows folder for duplicate files.

.DESCRIPTION
    Runs DuplicateTracker. If DuplicateTracker.exe sits next to this script, that
    exe is used. Otherwise the script builds the project in this folder with the
    .NET 10 SDK.

    With no -Delete and no -MoveTo, the program scans and then opens the review
    menu when you are at a console. Add -Quiet for a summary only.

.PARAMETER Path
    Folder to scan.

.PARAMETER Keep
    Which copy stays: newest, oldest, or shortest-path.

.PARAMETER Delete
    Remove the extra copies. One file in each group stays.

.PARAMETER Permanent
    Do not use the Recycle Bin. Requires -Delete.

.PARAMETER RemoveEmptyDirs
    After a delete or a move, remove folders that no longer contain anything.

.PARAMETER MoveTo
    Folder that receives the extra copies. A dated subfolder is created there.

.PARAMETER Export
    Report path. The extension must be .csv, .json, or .txt.

.PARAMETER Yes
    Apply -Delete or -MoveTo without asking.

.EXAMPLE
    .\Scan-Duplicates.ps1 D:\Photos

.EXAMPLE
    .\Scan-Duplicates.ps1 D:\Photos -Export D:\Reports\dupes.csv -FailOnDuplicates

.EXAMPLE
    .\Scan-Duplicates.ps1 D:\Photos -Delete -RemoveEmptyDirs -Yes
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Path,

    [ValidateSet('newest', 'oldest', 'shortest-path')]
    [string]$Keep = 'newest',

    [string]$MinSize = '',
    [string]$Extensions = '',
    [string]$ExcludeExtensions = '',
    [string]$ExcludeDirectories = '',
    [switch]$IncludeEmpty,
    [switch]$IncludeCloud,
    [switch]$IncludeCacheFiles,
    [switch]$Delete,
    [switch]$Permanent,
    [switch]$RemoveEmptyDirs,
    [string]$MoveTo = '',
    [string]$Export = '',
    [switch]$Yes,
    [switch]$DryRun,
    [switch]$FailOnDuplicates,
    [switch]$Quiet,
    [int]$Threads = 0
)

$ErrorActionPreference = 'Stop'

if ($Delete -and $MoveTo) {
    Write-Error 'Use -Delete or -MoveTo, not both.'
}

if ($Permanent -and -not $Delete) {
    Write-Error '-Permanent applies to -Delete.'
}

$forward = @('scan', $Path, '--keep', $Keep)
if ($MinSize) { $forward += @('--min-size', $MinSize) }
if ($Extensions) { $forward += @('--ext', $Extensions) }
if ($ExcludeExtensions) { $forward += @('--exclude-ext', $ExcludeExtensions) }
if ($ExcludeDirectories) { $forward += @('--exclude-dir', $ExcludeDirectories) }
if ($IncludeEmpty) { $forward += '--include-empty' }
if ($IncludeCloud) { $forward += '--include-cloud' }
if ($IncludeCacheFiles) { $forward += '--include-cache-files' }
if ($Delete) { $forward += '--delete' }
if ($Permanent) { $forward += '--permanent' }
if ($RemoveEmptyDirs) { $forward += '--remove-empty-dirs' }
if ($MoveTo) { $forward += @('--move', $MoveTo) }
if ($Export) { $forward += @('--export', $Export) }
if ($Yes) { $forward += '--yes' }
if ($DryRun) { $forward += '--dry-run' }
if ($FailOnDuplicates) { $forward += '--fail-on-duplicates' }
if ($Quiet) { $forward += '--quiet' }
if ($Threads -gt 0) { $forward += @('--threads', "$Threads") }

$exe = Join-Path $PSScriptRoot 'DuplicateTracker.exe'
if (Test-Path -LiteralPath $exe) {
    & $exe @forward
}
else {
    $project = Join-Path $PSScriptRoot 'DuplicateTracker.csproj'
    if (-not (Test-Path -LiteralPath $project)) {
        Write-Error 'DuplicateTracker.exe was not found next to this script.'
    }

    & dotnet run --project $project -c Release -- @forward
}

exit $LASTEXITCODE
