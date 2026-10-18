# Dot-sourced by package.ps1. Private keys stay in the Windows certificate provider.
function New-ClipNestSigningContext {
    param([string]$Thumbprint, [switch]$MachineStore, [string]$SignToolPath, [string]$TimestampServer)
    $thumbprintValue = ($Thumbprint -replace '\s', '').ToUpperInvariant()
    if ($thumbprintValue -notmatch '^[A-F0-9]{40}$') { throw 'Expected a 40-character certificate thumbprint.' }
    $store = if ($MachineStore) { 'LocalMachine' } else { 'CurrentUser' }
    $certificate = Get-Item "Cert:\$store\My\$thumbprintValue" -ErrorAction Stop
    if (!$certificate.HasPrivateKey) { throw 'The signing certificate has no accessible private key.' }
    if ($certificate.NotBefore -gt (Get-Date) -or $certificate.NotAfter -le (Get-Date)) { throw 'The signing certificate is not currently valid.' }
    if ('1.3.6.1.5.5.7.3.3' -notin @($certificate.EnhancedKeyUsageList | ForEach-Object { $_.ObjectId })) {
        throw 'The certificate must have the Code Signing extended key usage.'
    }
    if (!(Test-Path -LiteralPath $SignToolPath -PathType Leaf)) { throw 'SignTool was not found.' }
    $timestampUri = [uri]$TimestampServer
    if (!$timestampUri.IsAbsoluteUri -or $timestampUri.Scheme -notin @('http', 'https') -or $TimestampServer -match '[\s"]') {
        throw 'Expected an HTTP(S) RFC 3161 timestamp URL without whitespace.'
    }
    $signArguments = @('/sha1', $thumbprintValue, '/s', 'My', '/fd', 'SHA256', '/tr', $TimestampServer, '/td', 'SHA256')
    if ($MachineStore) { $signArguments += '/sm' }
    [pscustomobject]@{
        Thumbprint = $thumbprintValue
        Tool = (Resolve-Path -LiteralPath $SignToolPath).Path
        Arguments = $signArguments
    }
}

function Assert-ClipNestSignature {
    param([string]$Path, $Signing)
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or !$signature.SignerCertificate -or
        $signature.SignerCertificate.Thumbprint -ne $Signing.Thumbprint -or !$signature.TimeStamperCertificate) {
        throw "Missing, untrusted, unexpected or non-timestamped signature: $Path ($($signature.Status))"
    }
    & $Signing.Tool verify /pa /all $Path
    if ($LASTEXITCODE) { throw "Authenticode verification failed: $Path" }
}

function Invoke-ClipNestSign {
    param([string]$Path, $Signing)
    & $Signing.Tool sign @($Signing.Arguments) $Path
    if ($LASTEXITCODE) { throw "Code signing failed: $Path" }
    Assert-ClipNestSignature -Path $Path -Signing $Signing
}

function Assert-ClipNestUpdateSignatures {
    param([string]$Package, [string]$VerificationDirectory, $Signing)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    New-Item -ItemType Directory -Path $VerificationDirectory -ErrorAction Stop | Out-Null
    $archive = [IO.Compression.ZipFile]::OpenRead($Package)
    try {
        foreach ($name in @('ClipNest.exe', 'ClipNest.dll', 'ClipNest.Core.dll', 'ClipNest.Platform.dll', 'Squirrel.exe')) {
            $entry = $archive.GetEntry("lib/app/$name")
            if (!$entry) { throw "Required signed binary missing from update package: $name" }
            $destination = Join-Path $VerificationDirectory $name
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination)
            Assert-ClipNestSignature -Path $destination -Signing $Signing
        }
    } finally { $archive.Dispose() }
}
