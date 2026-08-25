<#
.SYNOPSIS
    Publishes launcher.exe and encoder.exe as single-file x86 executables.

.DESCRIPTION
    Produces exactly what a GitHub release ships: two self-extracting, single-file,
    framework-dependent executables plus a plain-text usage note. Nothing here reads
    or copies an operator's list.txt / config.ini / pack.properties — those hold
    server keys and never belong in a published artifact.

.PARAMETER OutputDirectory
    Where the finished executables land. Default: <repo>/artifacts/release.

.PARAMETER Zip
    Also produce Login38-<version>-win-x86.zip beside the output directory.

.EXAMPLE
    pwsh -File build/publish.ps1 -Zip
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [switch]$Zip
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo 'artifacts/release' }

# A stale output directory is worse than none: a removed file would survive into the zip.
if (Test-Path $OutputDirectory) { Remove-Item $OutputDirectory -Recurse -Force }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$projects = @(
    'src/Login38.App/Login38.App.csproj',
    'src/Login38.Encoder/Login38.Encoder.csproj'
)

foreach ($project in $projects) {
    Write-Host "publish $project" -ForegroundColor Cyan
    & dotnet publish (Join-Path $repo $project) `
        -c Release `
        -r win-x86 `
        --self-contained false `
        -p:PublishSingleFile=true `
        -o $OutputDirectory `
        --nologo
    if ($LASTEXITCODE -ne 0) { throw "publish failed: $project" }
}

# PublishSingleFile still emits the debug symbols next to the executable. They are not
# part of what a player installs, and shipping them makes the zip a third larger.
Get-ChildItem $OutputDirectory -Filter *.pdb -ErrorAction SilentlyContinue |
    Remove-Item -Force

Copy-Item (Join-Path $PSScriptRoot 'release-readme.txt') `
          (Join-Path $OutputDirectory '使用說明.txt') -Force

Write-Host ''
Get-ChildItem $OutputDirectory | Select-Object Name, @{
    Name = 'Size'; Expression = { '{0:N1} MB' -f ($_.Length / 1MB) }
} | Format-Table -AutoSize

if ($Zip) {
    $props = Get-Content (Join-Path $repo 'Directory.Build.props') -Raw
    $version = if ($props -match '<Version>([^<]+)</Version>') { $Matches[1] } else { '0.0.0' }

    $zipPath = Join-Path (Split-Path -Parent $OutputDirectory) "Login38-v$version-win-x86.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

    Compress-Archive -Path (Join-Path $OutputDirectory '*') -DestinationPath $zipPath
    Write-Host "zip -> $zipPath" -ForegroundColor Green
}
