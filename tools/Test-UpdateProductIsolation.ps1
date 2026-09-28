param(
    [Parameter(Mandatory=$true)][string]$RepositoryRoot,
    [Parameter(Mandatory=$true)][string]$OtherRepositoryRoot
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Web.Extensions
$flags = [Reflection.BindingFlags]'Static,Instance,Public,NonPublic'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$other = (Resolve-Path -LiteralPath $OtherRepositoryRoot).Path
$ownBytes = [IO.File]::ReadAllBytes((Join-Path $root 'update.json'))
$ownSignature = [IO.File]::ReadAllBytes((Join-Path $root 'update.json.sig'))
$otherBytes = [IO.File]::ReadAllBytes((Join-Path $other 'update.json'))
$otherSignature = [IO.File]::ReadAllBytes((Join-Path $other 'update.json.sig'))
$ownManifest = [Text.Encoding]::UTF8.GetString($ownBytes) | ConvertFrom-Json
$otherManifest = [Text.Encoding]::UTF8.GetString($otherBytes) | ConvertFrom-Json
$baseName = $ownManifest.ProductId.Split('/')[-1]
$otherName = $otherManifest.ProductId.Split('/')[-1]
$publicKey = [xml][IO.File]::ReadAllText((Join-Path $root 'update-public-key.xml'))
if ($publicKey.RSAKeyValue.ChildNodes.Count -ne 2 -or !$publicKey.RSAKeyValue.Modulus -or !$publicKey.RSAKeyValue.Exponent) { throw 'Public key contains unexpected fields' }
$otherPublicKey = [xml][IO.File]::ReadAllText((Join-Path $other 'update-public-key.xml'))
if ($publicKey.RSAKeyValue.Modulus -cne $otherPublicKey.RSAKeyValue.Modulus -or
    $publicKey.RSAKeyValue.Exponent -cne $otherPublicKey.RSAKeyValue.Exponent) { throw 'Publisher public keys differ' }
function Reject([scriptblock]$Action, [string]$Message) {
    try { & $Action } catch {
        $exception = $_.Exception
        while ($exception.InnerException) { $exception = $exception.InnerException }
        if ($exception -is [IO.InvalidDataException]) { return }
        throw
    }
    throw $Message
}
foreach ($suffix in '', 'Auto') {
    $name = $baseName + $suffix
    $path = Join-Path $root "build\Release\$name\$name.exe"
    $ns = if ($baseName -eq 'TayoPinball') { 'SoopPinballCollector' } else { $name }
    $assembly = [Reflection.Assembly]::LoadFrom($path)
    $updater = $assembly.GetType("$ns.SelfUpdater", $true)
    $manifestType = $assembly.GetType("$ns.UpdateManifest", $true)
    $verify = $updater.GetMethod('VerifyManifestSignature', $flags)
    $validate = $updater.GetMethod('ValidateManifestIdentity', $flags)
    foreach ($pair in @(@($ownBytes,$ownSignature), @($otherBytes,$otherSignature))) {
        $arguments = New-Object object[] 2
        $arguments[0] = [byte[]]$pair[0]
        $arguments[1] = [byte[]]$pair[1]
        $verify.Invoke($null,$arguments) | Out-Null
    }
    $manifest = [Activator]::CreateInstance($manifestType,$true)
    $manifest.SchemaVersion = 2
    $manifest.ProductId = $ownManifest.ProductId
    $validate.Invoke($null,@($manifest)) | Out-Null
    foreach ($wrongId in @($otherManifest.ProductId, '', $null, 'Gyeon-ai/Unknown')) {
        $manifest.ProductId = $wrongId
        Reject { $validate.Invoke($null,@($manifest)) | Out-Null } 'A different or missing product ID was accepted'
    }
    $identity = $updater.GetMethod('ValidateAssemblyIdentity',$flags)
    $otherExe = Join-Path $other "build\Release\$otherName$suffix\$otherName$suffix.exe"
    $identityArguments = New-Object object[] 3
    $identityArguments[0] = $otherExe.PSObject.BaseObject
    $identityArguments[1] = $name.PSObject.BaseObject
    $identityArguments[2] = $null
    Reject { $identity.Invoke($null,$identityArguments) | Out-Null } 'An EXE from the other product was accepted'
    $embedded = $assembly.GetManifestResourceStream($baseName + 'UpdatePublicKey')
    try {
        $reader = New-Object IO.StreamReader($embedded)
        $embeddedKey = [xml]$reader.ReadToEnd()
        if ($embeddedKey.OuterXml -ne $publicKey.OuterXml) { throw 'Embedded public key differs' }
    } finally { $embedded.Dispose() }
    Write-Output "PASS: $name shared signer, product separation, cross-product EXE rejection, public-only key"
}
