[CmdletBinding()]
param([string]$Configuration = 'Release', [string]$Version = '0.2.3')
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path "$PSScriptRoot/..").Path
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (!$dotnet) { $dotnet = Get-Command "$env:USERPROFILE/.dotnet/dotnet.exe" -ErrorAction Stop }
Push-Location $projectRoot
try {
    & $dotnet.Source build ClipNest.Windows.sln -c $Configuration --nologo
    if ($LASTEXITCODE) { throw 'Build failed' }
    & $dotnet.Source test tests/ClipNest.Core.Tests -c $Configuration --no-build --nologo
    if ($LASTEXITCODE) { throw 'Tests failed' }
    # Use a fresh staging folder, avoiding accidental deletion of any existing release.
    $staging = Join-Path $projectRoot ('dist/staging-' + [guid]::NewGuid().ToString('N'))
    $portable = Join-Path $staging 'ClipNest'
    & $dotnet.Source publish src/ClipNest.App -c $Configuration -r win-x64 --self-contained true -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false -p:Version=$Version -o $portable --nologo
    if ($LASTEXITCODE) { throw 'Publish failed' }
    Copy-Item README.md "$portable/使用说明.md"
    Copy-Item UPSTREAM.md "$portable/UPSTREAM.md"
    $zip = Join-Path $projectRoot "dist/ClipNest-$Version-win-x64.zip"
    Compress-Archive -LiteralPath $portable -DestinationPath $zip -Force
    (Get-FileHash $zip -Algorithm SHA256).Hash + '  ' + [IO.Path]::GetFileName($zip) | Set-Content "$zip.sha256"
    Write-Host "Portable program: $portable/ClipNest.exe"
    Write-Host "ZIP: $zip"
} finally { Pop-Location }
