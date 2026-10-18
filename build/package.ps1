[CmdletBinding()]
param([string]$Configuration = 'Release', [string]$Version = '0.4.0', [switch]$SkipInstaller)
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
    Copy-Item CHANGELOG.md "$portable/CHANGELOG.md"
    New-Item -ItemType Directory -Path "$portable/docs", "$portable/design" -Force | Out-Null
    Copy-Item docs/media "$portable/docs/media" -Recurse
    Copy-Item docs/updates.md "$portable/docs/updates.md"
    Get-ChildItem docs -Filter 'testing-*.md' | Copy-Item -Destination "$portable/docs"
    Copy-Item design/clipnest-512.png "$portable/design/clipnest-512.png"
    $zip = Join-Path $projectRoot "dist/ClipNest-$Version-win-x64.zip"
    Compress-Archive -LiteralPath $portable -DestinationPath $zip -Force
    (Get-FileHash $zip -Algorithm SHA256).Hash + '  ' + [IO.Path]::GetFileName($zip) | Set-Content "$zip.sha256"
    Write-Host "Portable program: $portable/ClipNest.exe"
    Write-Host "ZIP: $zip"
    if (!$SkipInstaller) {
        $env:DOTNET_ROOT = Split-Path $dotnet.Source
        & $dotnet.Source tool restore
        if ($LASTEXITCODE) { throw 'Velopack tool restore failed' }
        $setupOutput = Join-Path $staging 'setup'
        & $dotnet.Source tool run vpk -- pack --packId ClipNest.Windows --packTitle '拾贴 ClipNest' --packAuthors 'Karl-Dai' --packVersion $Version --packDir $portable --mainExe ClipNest.exe --outputDir $setupOutput --runtime win-x64 --channel win --shortcuts Desktop,StartMenuRoot --noPortable --delta None --icon src/ClipNest.App/Assets/app.ico --releaseNotes build/release-notes.md --instWelcome build/installer-welcome.txt
        if ($LASTEXITCODE) { throw 'Installer packaging failed' }
        $setup = Get-ChildItem $setupOutput -Filter '*Setup.exe' | Select-Object -First 1
        if (!$setup) { throw 'Installer was not generated' }
        $installer = Join-Path $projectRoot "dist/ClipNest-$Version-Setup-x64.exe"
        Copy-Item $setup.FullName $installer
        $updateAssets = @(Get-ChildItem $setupOutput -File | Where-Object { $_.Extension -eq '.nupkg' -or $_.Name -in @('releases.win.json', 'RELEASES') })
        $updateAssets | Copy-Item -Destination (Join-Path $projectRoot 'dist')
        $assets = @(Get-Item $installer, $zip) + $updateAssets
        $assets | ForEach-Object { (Get-FileHash $_.FullName -Algorithm SHA256).Hash + '  ' + $_.Name } | Set-Content (Join-Path $projectRoot "dist/SHA256SUMS-$Version.txt") -Encoding ASCII
        Write-Host "Installer: $installer"
    }
} finally { Pop-Location }
