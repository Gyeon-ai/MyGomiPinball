param(
    [string]$ManifestPath,
    [string]$SignaturePath,
    [string]$PublicKeyPath
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if ([String]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $root 'update.json'
}
if ([String]::IsNullOrWhiteSpace($SignaturePath)) {
    $SignaturePath = Join-Path $root 'update.json.sig'
}
if ([String]::IsNullOrWhiteSpace($PublicKeyPath)) {
    $PublicKeyPath = Join-Path $root 'update-public-key.xml'
}

foreach ($path in @($ManifestPath, $SignaturePath, $PublicKeyPath)) {
    if (!(Test-Path -LiteralPath $path)) {
        throw "Required update file was not found: $path"
    }
}

function New-RsaProvider {
    $parameters = New-Object Security.Cryptography.CspParameters
    $parameters.ProviderType = 24
    $provider = New-Object Security.Cryptography.RSACryptoServiceProvider($parameters)
    $provider.PersistKeyInCsp = $false
    return $provider
}

$manifestBytes = [IO.File]::ReadAllBytes($ManifestPath)
$signatureText = [IO.File]::ReadAllText($SignaturePath).Trim()
$signatureBytes = [Convert]::FromBase64String($signatureText)
$publicKeyXml = [IO.File]::ReadAllText($PublicKeyPath)
$sha256Oid = [Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256')

$rsa = New-RsaProvider
try {
    $rsa.FromXmlString($publicKeyXml)
    if (!$rsa.VerifyData($manifestBytes, $sha256Oid, $signatureBytes)) {
        throw 'The update manifest signature is invalid.'
    }

    $tamperedBytes = New-Object byte[] $manifestBytes.Length
    [Array]::Copy($manifestBytes, $tamperedBytes, $manifestBytes.Length)
    $tamperedBytes[0] = $tamperedBytes[0] -bxor 1
    if ($rsa.VerifyData($tamperedBytes, $sha256Oid, $signatureBytes)) {
        throw 'Tampered update manifest was incorrectly accepted.'
    }
}
finally {
    $rsa.Dispose()
}

Write-Host 'Update signature is valid and tampering is rejected.'
