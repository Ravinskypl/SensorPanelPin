<#
.SYNOPSIS
    Builds the release package: tests, a self-contained publish, and a ZIP with install scripts and
    SHA-256 checksums in .\artifacts.

.DESCRIPTION
    The same script runs locally and in the release workflow, so a downloaded ZIP can be rebuilt and
    compared. The version comes from <Version> in SensorPanelPin.csproj.

    The publish is self-contained (no .NET install needed) and single-file, but deliberately WITHOUT
    IncludeNativeLibrariesForSelfExtract: with it, the five native WPF libraries are unpacked at every
    start into the user's %TEMP%, and the program, which runs with administrator rights, would load
    them from a folder any program on the account can write to. They ship beside the exe instead.

.PARAMETER SkipTests
    Skip dotnet test, for a quick local package.
#>
param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src\SensorPanelPin\SensorPanelPin.csproj'
$tests = Join-Path $root 'tests\SensorPanelPin.Tests\SensorPanelPin.Tests.csproj'
$artifacts = Join-Path $root 'artifacts'

$version = ([xml](Get-Content -Raw $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> in $project" }
$name = "SensorPanelPin-$version-win-x64"
$staging = Join-Path $artifacts $name

if (-not $SkipTests) {
    & dotnet test $tests -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
}

if (Test-Path $artifacts) { Remove-Item -LiteralPath $artifacts -Recurse -Force }
New-Item -ItemType Directory -Path $staging | Out-Null

& dotnet publish $project -c Release -o $staging --nologo `
    -p:PublishSingleFile=true -p:SelfContained=true -p:EnableCompressionInSingleFile=true -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

foreach ($file in 'Install.cmd', 'Install.ps1', 'Uninstall.cmd', 'Uninstall.ps1') {
    Copy-Item (Join-Path $PSScriptRoot $file) $staging
}
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $staging 'LICENSE.txt')
Copy-Item (Join-Path $PSScriptRoot 'README-release.txt') (Join-Path $staging 'README.txt')

$zip = Join-Path $artifacts "$name.zip"
Compress-Archive -Path $staging -DestinationPath $zip -CompressionLevel Optimal

$hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
Set-Content -Path (Join-Path $artifacts 'SHA256SUMS.txt') -Value "$hash  $name.zip" -Encoding ascii

Write-Host "Package: $zip"
Write-Host "SHA-256: $hash"
