param(
    [ValidateSet('win-x64','win-arm64','osx-x64','osx-arm64','linux-x64','linux-arm64')][string]$Runtime = 'win-x64',
    [string]$OutputDirectory = 'artifacts/SERVER',
    [switch]$NoRestore
)
$ErrorActionPreference = 'Stop'
if (!$OutputDirectory) { $OutputDirectory = 'artifacts/SERVER' }
$output = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $PSScriptRoot $OutputDirectory }
# One self-contained executable: the server hosts the Blazor interface, the REST API, the WebSocket and its own cloud workers.
$publishArgs = @((Join-Path $PSScriptRoot 'src\MonolithHarness.Server\MonolithHarness.Server.csproj'), '-c', 'Release', '-r', $Runtime,
    '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeAllContentForSelfExtract=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $output)
if ($NoRestore) { $publishArgs += '--no-restore' }
dotnet publish @publishArgs
if ($LASTEXITCODE -ne 0) { throw 'Server publication failed.' }
Write-Host "Monolith Harness Server available in $output/MonolithHarness$(if ($Runtime.StartsWith('win-')) { '.exe' })"
