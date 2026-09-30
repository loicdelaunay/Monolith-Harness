param(
    [ValidateSet('win-x64','win-arm64','osx-x64','osx-arm64')][string]$Runtime = 'win-x64',
    [string]$OutputDirectory = '',
    [switch]$NoRestore
)
$ErrorActionPreference = 'Stop'
if (!$OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot "artifacts\release\cli\$Runtime" }
$output = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $PSScriptRoot $OutputDirectory }
$publishArgs = @((Join-Path $PSScriptRoot 'src\OhMyHarness.Cli\OhMyHarness.Cli.csproj'), '-c', 'Release', '-r', $Runtime,
    '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeAllContentForSelfExtract=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $output)
if ($NoRestore) { $publishArgs += '--no-restore' }
dotnet publish @publishArgs
if ($LASTEXITCODE -ne 0) { throw 'CLI publication failed.' }
Write-Host "Monolith Harness CLI available in $output"
