[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Version = '0.4.1',
    [switch]$SkipInstaller,
    [string]$SignCertificateThumbprint = $env:CLIPNEST_SIGN_CERT_THUMBPRINT,
    [string]$SignToolPath,
    [switch]$SignMachineStore,
    [string]$TimestampServer = 'http://timestamp.digicert.com',
    [switch]$RequireSignature
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path "$PSScriptRoot/..").Path
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (!$dotnet) { $dotnet = Get-Command "$env:USERPROFILE/.dotnet/dotnet.exe" -ErrorAction Stop }
Push-Location $projectRoot
try {
    . "$PSScriptRoot/signing.ps1"
    $signing = $null
    if ($RequireSignature -and !$SignCertificateThumbprint) {
        throw 'A publicly trusted code-signing certificate is required. Set SignCertificateThumbprint; unsigned release refused.'
    }
    if ($SignCertificateThumbprint) {
        & $dotnet.Source tool restore
        if ($LASTEXITCODE) { throw 'Velopack tool restore failed' }
        if (!$SignToolPath) {
            $nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
            $toolVersion = (Get-Content .config/dotnet-tools.json -Raw | ConvertFrom-Json).tools.vpk.version
            $SignToolPath = Join-Path $nugetRoot "vpk/$toolVersion/vendor/signing/signtool.exe"
        }
        $signing = New-ClipNestSigningContext -Thumbprint $SignCertificateThumbprint -MachineStore:$SignMachineStore -SignToolPath $SignToolPath -TimestampServer $TimestampServer
    }
    & $dotnet.Source build ClipNest.Windows.sln -c $Configuration -p:ClipNestDiagnostics=false --nologo
    if ($LASTEXITCODE) { throw 'Build failed' }
    & $dotnet.Source test tests/ClipNest.Core.Tests -c $Configuration --no-build --nologo
    if ($LASTEXITCODE) { throw 'Tests failed' }
    # Use a fresh staging folder, avoiding accidental deletion of any existing release.
    $staging = Join-Path $projectRoot ('dist/staging-' + [guid]::NewGuid().ToString('N'))
    $portable = Join-Path $staging 'ClipNest'
    & $dotnet.Source publish src/ClipNest.App -c $Configuration -r win-x64 --self-contained true -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false -p:ClipNestDiagnostics=false -p:Version=$Version -o $portable --nologo
    if ($LASTEXITCODE) { throw 'Publish failed' }
    if ([Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes((Join-Path $portable 'ClipNest.dll'))).Contains('RunDiagnosticsAsync')) {
        throw 'Diagnostic runtime must never be included in a distributable build.'
    }
    if ($signing) {
        foreach ($name in @('ClipNest.exe', 'ClipNest.dll', 'ClipNest.Core.dll', 'ClipNest.Platform.dll')) {
            Invoke-ClipNestSign -Path (Join-Path $portable $name) -Signing $signing
        }
    }
    Copy-Item README.md "$portable/使用说明.md"
    Copy-Item UPSTREAM.md "$portable/UPSTREAM.md"
    Copy-Item CHANGELOG.md "$portable/CHANGELOG.md"
    New-Item -ItemType Directory -Path "$portable/docs", "$portable/design" -Force | Out-Null
    Copy-Item docs/media "$portable/docs/media" -Recurse
    Copy-Item docs/updates.md "$portable/docs/updates.md"
    Copy-Item docs/signing.md "$portable/docs/signing.md"
    Get-ChildItem docs -Filter 'testing-*.md' | Copy-Item -Destination "$portable/docs"
    Get-ChildItem docs -Filter 'performance-*.md' | Copy-Item -Destination "$portable/docs"
    if (Test-Path docs/performance) { Copy-Item docs/performance "$portable/docs/performance" -Recurse }
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
        $signOptions = @()
        if ($signing) {
            # Velopack must also sign its generated updater and installer during packing.
            $signOptions = @('--signParams', ($signing.Arguments -join ' '), '--signParallel', '1')
        }
        & $dotnet.Source tool run vpk -- pack --packId ClipNest.Windows --packTitle '拾贴 ClipNest' --packAuthors 'Karl-Dai' --packVersion $Version --packDir $portable --mainExe ClipNest.exe --outputDir $setupOutput --runtime win-x64 --channel win --shortcuts Desktop,StartMenuRoot --noPortable --delta None --icon src/ClipNest.App/Assets/app.ico --releaseNotes build/release-notes.md --instWelcome build/installer-welcome.txt @signOptions
        if ($LASTEXITCODE) { throw 'Installer packaging failed' }
        $setup = Get-ChildItem $setupOutput -Filter '*Setup.exe' | Select-Object -First 1
        if (!$setup) { throw 'Installer was not generated' }
        if ($signing) {
            Assert-ClipNestSignature -Path $setup.FullName -Signing $signing
            $fullPackage = Get-Item (Join-Path $setupOutput "ClipNest.Windows-$Version-full.nupkg")
            Assert-ClipNestUpdateSignatures -Package $fullPackage.FullName -VerificationDirectory (Join-Path $staging 'signature-verification') -Signing $signing
        }
        $installer = Join-Path $projectRoot "dist/ClipNest-$Version-Setup-x64.exe"
        Copy-Item $setup.FullName $installer
        $updateAssets = @(Get-ChildItem $setupOutput -File | Where-Object { $_.Extension -eq '.nupkg' -or $_.Name -in @('releases.win.json', 'RELEASES') })
        $updateAssets | Copy-Item -Destination (Join-Path $projectRoot 'dist')
        $assets = @(Get-Item $installer, $zip) + $updateAssets
        $assets | ForEach-Object { (Get-FileHash $_.FullName -Algorithm SHA256).Hash + '  ' + $_.Name } | Set-Content (Join-Path $projectRoot "dist/SHA256SUMS-$Version.txt") -Encoding ASCII
        Write-Host "Installer: $installer"
    }
} finally { Pop-Location }
