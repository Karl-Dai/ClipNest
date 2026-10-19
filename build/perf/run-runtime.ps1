[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$DataRoot,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(0.1,1440)][double]$Minutes = 60,
    [switch]$PauseInstalledApp,
    [switch]$SkipClipboard
)
$ErrorActionPreference = 'Stop'
$app = (Resolve-Path -LiteralPath $AppPath).Path
$assembly = Join-Path (Split-Path $app) 'ClipNest.dll'
if (!(Test-Path -LiteralPath $assembly) -or ![Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($assembly)).Contains('RunDiagnosticsAsync')) {
    throw 'Use a dedicated build published with -p:ClipNestDiagnostics=true.'
}
$data = (Resolve-Path -LiteralPath $DataRoot).Path
if (!(Test-Path -LiteralPath (Join-Path $data 'ClipNest/diagnostic-fixture.json'))) { throw 'Synthetic fixture marker missing.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a new output directory.' }
$installed = Join-Path $env:LOCALAPPDATA 'ClipNest.Windows/current/ClipNest.exe'
$running = @(Get-Process ClipNest -ErrorAction SilentlyContinue)
$paused = $false
if ($running.Count -gt 0 -and !$SkipClipboard) {
    if (!$PauseInstalledApp) { throw 'Close ClipNest first or explicitly pass -PauseInstalledApp.' }
    if (@($running | Where-Object { $_.Path -ne $installed }).Count -gt 0) { throw 'A different ClipNest build is running; close it explicitly first.' }
    $quit = Start-Process -FilePath $installed -ArgumentList '--quit' -WindowStyle Hidden -PassThru
    $quit.WaitForExit(10000) | Out-Null
    foreach ($process in $running) { if (!$process.WaitForExit(10000)) { throw 'Installed app did not exit gracefully.' } }
    $paused = $true
}
$test = $null
try {
    $env:CLIPNEST_DATA_ROOT = $data
    $env:CLIPNEST_DIAGNOSTICS_OUTPUT = $output
    $env:CLIPNEST_SOAK_MINUTES = $Minutes.ToString([Globalization.CultureInfo]::InvariantCulture)
    $env:CLIPNEST_DIAGNOSTICS_SKIP_CLIPBOARD = if ($SkipClipboard) { '1' } else { '0' }
    $test = Start-Process -FilePath $app -ArgumentList '--diagnostics' -WindowStyle Hidden -PassThru
    Write-Output "Runtime diagnostics PID=$($test.Id), requested soak=$Minutes minutes"
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $installedRestored = $false
    while (!$test.HasExited) {
        Start-Sleep -Seconds 2
        $test.Refresh()
        $progressPath = Join-Path $output 'progress.json'
        if (!$installedRestored -and $paused -and (Test-Path -LiteralPath $progressPath)) {
            try { $progress = Get-Content -LiteralPath $progressPath -Raw | ConvertFrom-Json } catch { $progress = $null }
            if ($progress.Phase -eq 'soak') {
                Remove-Item Env:\CLIPNEST_DATA_ROOT,Env:\CLIPNEST_DIAGNOSTICS_OUTPUT,Env:\CLIPNEST_SOAK_MINUTES,Env:\CLIPNEST_DIAGNOSTICS_SKIP_CLIPBOARD -ErrorAction SilentlyContinue
                Start-Process -FilePath $installed -ArgumentList '--background' -WindowStyle Hidden
                $installedRestored = $true
                Write-Output 'Installed app restored; isolated diagnostics continue in the background.'
            }
        }
        if ($watch.Elapsed.TotalMinutes -gt $Minutes + 5) { throw 'Runtime diagnostics exceeded its deadline.' }
    }
    $resultPath = Join-Path $output 'result.json'
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    $result | Select-Object Status,StartedUtc,EndedUtc,RequestedSoakMinutes,ClipboardPhasesSkipped,Checks,Failures,ImageSwitchP95Ms,ImageSwitchMaxMs | ConvertTo-Json -Depth 5
    if ($result.Status -ne 'passed') { throw 'Runtime diagnostics failed; inspect result.json.' }
} finally {
    Remove-Item Env:\CLIPNEST_DATA_ROOT,Env:\CLIPNEST_DIAGNOSTICS_OUTPUT,Env:\CLIPNEST_SOAK_MINUTES,Env:\CLIPNEST_DIAGNOSTICS_SKIP_CLIPBOARD -ErrorAction SilentlyContinue
    if ($test -and !$test.HasExited) {
        $env:CLIPNEST_DATA_ROOT = $data
        $quit = Start-Process -FilePath $app -ArgumentList '--diagnostics','--quit' -WindowStyle Hidden -PassThru
        $quit.WaitForExit(10000) | Out-Null
        if (!$test.WaitForExit(10000)) { Stop-Process -Id $test.Id }
        Remove-Item Env:\CLIPNEST_DATA_ROOT -ErrorAction SilentlyContinue
    }
    if ($paused -and !(Get-Process ClipNest -ErrorAction SilentlyContinue)) {
        Start-Process -FilePath $installed -ArgumentList '--background' -WindowStyle Hidden
    }
}
