param(
    [string]$OutputDirectory = 'artifacts/TEMP/android-flutter',
    [switch]$NoRestore,
    [switch]$RequireReleaseSigning,
    [string]$FlutterExecutable = 'flutter'
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src/MonolithHarness.App.Flutter'
$output = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $PSScriptRoot $OutputDirectory }
$flutterArgs = @('build', 'apk', '--release', '--target', 'lib/main.dart', '--target-platform', 'android-arm64')
if ($NoRestore) { $flutterArgs += '--no-pub' }
$previousRequirement = $env:MONOLITH_REQUIRE_RELEASE_SIGNING
if ($RequireReleaseSigning) { $env:MONOLITH_REQUIRE_RELEASE_SIGNING = 'true' }
Push-Location -LiteralPath $project
try {
    & $FlutterExecutable @flutterArgs
    if ($LASTEXITCODE -ne 0) { throw 'Flutter Android publication failed.' }
    $apk = Join-Path $project 'build/app/outputs/flutter-apk/app-release.apk'
    if (!(Test-Path -LiteralPath $apk -PathType Leaf)) { throw 'Flutter APK not found.' }
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $version = (Select-String -Path (Join-Path $project 'pubspec.yaml') -Pattern '^version: ([^+]+)').Matches[0].Groups[1].Value
    $name = if ($RequireReleaseSigning) { "MonolithHarness-Flutter-v$version-android-arm64.apk" } else { 'Monolith-Flutter-development.apk' }
    Copy-Item -LiteralPath $apk -Destination (Join-Path $output $name)
    if ($RequireReleaseSigning) {
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $output $name)).Hash.ToLowerInvariant()
        [IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS-android.txt'), "$hash  $name`n", [Text.Encoding]::ASCII)
    }
} finally { Pop-Location; $env:MONOLITH_REQUIRE_RELEASE_SIGNING = $previousRequirement }
Write-Host "Flutter APK available in $output."
