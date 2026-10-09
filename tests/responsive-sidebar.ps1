param([int]$TimeoutSeconds = 120)
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path -Parent $PSScriptRoot
$smokeRunPath = Join-Path $repoPath ('.tmp/responsive-sidebar-' + [Guid]::NewGuid().ToString('N'))
$guiPath = Join-Path $repoPath 'artifacts/GUI/MonolithHarness.exe'
if (!(Test-Path -LiteralPath $guiPath)) { throw 'Publiez la GUI avant de lancer ce test.' }
New-Item -ItemType Directory -Path $smokeRunPath | Out-Null
$previousSmoke = $env:MONOLITHHARNESS_UI_SMOKE
$previousResponsive = $env:MONOLITHHARNESS_RESPONSIVE_SMOKE
try {
    $env:MONOLITHHARNESS_UI_SMOKE = $smokeRunPath
    $env:MONOLITHHARNESS_RESPONSIVE_SMOKE = '1'
    $smokeProcess = Start-Process -FilePath $guiPath -WorkingDirectory $smokeRunPath -WindowStyle Hidden -PassThru
    Write-Output ('Rapport : ' + $smokeRunPath)
} finally {
    $env:MONOLITHHARNESS_UI_SMOKE = $previousSmoke
    $env:MONOLITHHARNESS_RESPONSIVE_SMOKE = $previousResponsive
}
$smokeDeadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
while ([DateTime]::UtcNow -lt $smokeDeadline) {
    $errorPath = Join-Path $smokeRunPath 'smoke-error.txt'
    $resultPath = Join-Path $smokeRunPath 'smoke-ok.txt'
    if (Test-Path -LiteralPath $errorPath) { throw (Get-Content -LiteralPath $errorPath -Raw) }
    if (Test-Path -LiteralPath $resultPath) {
        Write-Output ('Vérifications réussies : ' + (Get-Content -LiteralPath $resultPath).Count)
        return
    }
    Start-Sleep -Milliseconds 500
}
throw ('Le test a dépassé son délai. Consulter : ' + $smokeRunPath)
