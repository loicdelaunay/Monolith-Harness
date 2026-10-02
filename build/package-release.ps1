param(
    [Parameter(Mandatory)][ValidateSet('win-x64', 'linux-x64')][string]$Runtime
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
[xml]$guiProject = Get-Content (Join-Path $repo 'src/MonolithHarness.App/MonolithHarness.App.csproj')
[xml]$cliProject = Get-Content (Join-Path $repo 'src/MonolithHarness.Cli/MonolithHarness.Cli.csproj')
$version = [string]($guiProject.Project.PropertyGroup.ApplicationDisplayVersion | Where-Object { $_ } | Select-Object -First 1)
$cliVersion = [string]($cliProject.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if (!$version -or $version -ne $cliVersion) { throw 'GUI and CLI versions must match.' }
$root = Join-Path $repo 'artifacts/TEMP/release-packages'
$windows = $Runtime.StartsWith('win-')
foreach ($channel in @('GUI', 'CLI')) {
    $exe = 'MonolithHarness'
    if ($windows) { $exe += '.exe' }
    $source = Join-Path $repo "artifacts/$channel/$exe"
    if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing executable: $source" }
    $stage = Join-Path $root "stage-$channel-$Runtime"
    if (Test-Path -LiteralPath $stage) { throw "Staging directory already exists: $stage" }
    $output = Join-Path $root $channel
    New-Item -ItemType Directory -Path $stage, $output -Force | Out-Null
    try {
        # Deliberate allowlist: never package the entire portable workspace.
        Copy-Item -LiteralPath $source -Destination (Join-Path $stage $exe)
        Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $stage
        $start = if ($channel -eq 'CLI') { "Run ./$exe from a terminal, then /connect to configure your provider." } else { "Run ./$exe and open Settings > Providers." }
        $platformNote = if ($Runtime -eq 'linux-x64') {
            'Linux x64 (tested on Fedora 44). Use an X11 desktop for the GUI; WebKitGTK and GTK3 are needed for the browser. No separate .NET or Python installation is required. Desktop control tools are not available.'
        } else { 'Windows x64. No separate .NET or Python installation is required.' }
        @"
Monolith Harness $channel $version ($Runtime)

Extract into a writable folder. $start
$platformNote

To update, close all application instances and replace only $exe. Back up and keep
database.sqlite, workspace/ and existing resources. This download includes no user data.
Startup groups managed resource folders in workspace/; folderless chats get their
own workspace/conversations/chat-ID/ directory, with files retained across restarts.
The chat model is not bundled; configure a provider. Optional integrations
such as Git, Docker/Podman, OpenCode and MCP retain their own prerequisites.

https://github.com/loicdelaunay/Monolith-Harness
"@ | Set-Content -LiteralPath (Join-Path $stage 'README.txt') -Encoding utf8
        $prefix = if ($channel -eq 'GUI') { 'MonolithHarness' } else { 'MonolithHarness-CLI' }
        $extension = if ($windows) { 'zip' } else { 'tar.gz' }
        $archive = Join-Path $output "$prefix-v$version-$Runtime.$extension"
        if (Test-Path -LiteralPath $archive) { throw "Archive already exists: $archive" }
        if ($windows) {
            Compress-Archive -LiteralPath @((Join-Path $stage $exe), (Join-Path $stage 'LICENSE'), (Join-Path $stage 'README.txt')) -DestinationPath $archive
        } else {
            & chmod +x (Join-Path $stage $exe)
            if ($LASTEXITCODE -ne 0) { throw 'Cannot set executable permissions.' }
            & tar -czf $archive -C $stage $exe LICENSE README.txt
            if ($LASTEXITCODE -ne 0) { throw 'Archive creation failed.' }
        }
        $sum = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        $checksums = @("$sum  $([IO.Path]::GetFileName($archive))")
        if ($windows) {
            # Older installed update clients recognize only the historical archive prefix.
            $legacyNames = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'LegacyReleaseNames.json') -Raw | ConvertFrom-Json
            $legacyArchive = Join-Path $output "$($legacyNames.$channel)-v$version-$Runtime.$extension"
            if (Test-Path -LiteralPath $legacyArchive) { throw "Compatibility archive already exists: $legacyArchive" }
            Copy-Item -LiteralPath $archive -Destination $legacyArchive
            $checksums += "$sum  $([IO.Path]::GetFileName($legacyArchive))"
        }
        $checksums | Set-Content -LiteralPath (Join-Path $output "SHA256SUMS-$Runtime.txt") -Encoding ascii
        Write-Host "Packaged $channel $version ($Runtime): $archive"
    } finally {
        $resolved = [IO.Path]::GetFullPath($stage)
        if ([IO.Path]::GetDirectoryName($resolved) -ne [IO.Path]::GetFullPath($root)) { throw 'Unsafe staging cleanup path.' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
