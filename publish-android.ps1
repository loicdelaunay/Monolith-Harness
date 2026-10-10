param([string]$OutputDirectory = 'artifacts/TEMP/android-portable', [switch]$NoRestore)
$ErrorActionPreference = 'Stop'
$output = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $PSScriptRoot $OutputDirectory }
$publishArgs = @((Join-Path $PSScriptRoot 'src/MonolithHarnessGui.Portable/MonolithHarnessGui.Portable.csproj'), '-c', 'Release', '-f', 'net10.0-android', '-r', 'android-arm64', '-p:AndroidPackageFormats=apk', '-p:RunAOTCompilation=false', '-o', $output)
if ($NoRestore) { $publishArgs += '--no-restore' }
dotnet publish @publishArgs
if ($LASTEXITCODE -ne 0) { throw 'Android publication failed.' }
Write-Host "Android preview available in $output. Stable releases require a persistent signing key."
