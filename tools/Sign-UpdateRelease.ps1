param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ReleaseNotes,

    [string]$SigningKeyPath = (Join-Path $env:LOCALAPPDATA 'Gyeona\TayoPinball\Signing\update-signing-key.dat'),

    [switch]$ReSignCurrentVersion
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security

$root = Split-Path -Parent $PSScriptRoot
$statePath = Join-Path $PSScriptRoot 'ReleaseState.json'
$publicKeyPath = Join-Path $root 'update-public-key.xml'
$manifestPath = Join-Path $root 'update.json'
$signaturePath = Join-Path $root 'update.json.sig'
$utf8NoBom = New-Object Text.UTF8Encoding($false)
$entropy = [Text.Encoding]::UTF8.GetBytes('TayoPinball update signing key v1')
$displayBase = -join @([char]0xACF0, [char]0xC774, [char]0x0020, [char]0xC885, [char]0xAC9C, [char]0xD540, [char]0xBCFC)
$projects = @(
    [pscustomobject]@{
        Name = 'MyGomiPinball'
        Key = 'Standard'
        KoreanName = "$displayBase.exe"
    },
    [pscustomobject]@{
        Name = 'MyGomiPinballAuto'
        Key = 'Auto'
        KoreanName = "$displayBase $([char]0xC790)$([char]0xB3D9).exe"
    }
)

foreach ($path in @($SigningKeyPath, $statePath, $publicKeyPath)) {
    if (!(Test-Path -LiteralPath $path)) {
        throw "Required release file was not found: $path"
    }
}
if ([String]::IsNullOrWhiteSpace($ReleaseNotes.Trim())) {
    throw 'Release notes cannot be blank.'
}

$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
$version = $null
$releaseData = @{}
$outputs = New-Object Collections.Generic.List[object]
foreach ($project in $projects) {
    $projectState = $state.PSObject.Properties[$project.Name].Value
    if ($null -eq $projectState -or [int]$projectState.Number -lt 1) {
        throw "Release state is invalid for $($project.Name)."
    }
    $builtExe = Join-Path $root "build\Release\$($project.Name)\$($project.Name).exe"
    $numberedExe = Join-Path $root ("dist\Release\{0}\{0}-{1:000}.exe" -f $project.Name, [int]$projectState.Number)
    foreach ($path in @($builtExe, $numberedExe)) {
        if (!(Test-Path -LiteralPath $path)) { throw "Release executable was not found: $path" }
    }
    $assembly = [Reflection.AssemblyName]::GetAssemblyName($builtExe)
    if ($assembly.Name -cne $project.Name -or
        $assembly.Version.ToString() -ne [string]$projectState.Version) {
        throw "Built identity does not match release state for $($project.Name)."
    }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $builtExe).Hash -ne
        (Get-FileHash -Algorithm SHA256 -LiteralPath $numberedExe).Hash) {
        throw "Numbered artifact differs from the built executable: $numberedExe"
    }
    if ($null -eq $version) { $version = $assembly.Version }
    elseif ($version -ne $assembly.Version) { throw 'General and Auto versions differ.' }

    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $numberedExe).Hash.ToUpperInvariant()
    $releaseData[$project.Key] = [ordered]@{
        Url = 'https://github.com/Gyeon-ai/MyGomiPinball/raw/refs/heads/main/' +
            [Uri]::EscapeDataString($project.KoreanName)
        Sha256 = $hash
        Size = (Get-Item -LiteralPath $numberedExe).Length
    }
    $outputs.Add([pscustomobject]@{ Source = $numberedExe; Destination = (Join-Path $root $project.KoreanName) })
    $outputs.Add([pscustomobject]@{ Source = $numberedExe; Destination = (Join-Path $root ($project.Name + '.exe')) })
}

if (Test-Path -LiteralPath $manifestPath) {
    $previous = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $sameRelease = $version -eq [Version]$previous.Version -and
        $previous.ProductId -ceq 'Gyeon-ai/MyGomiPinball' -and
        $previous.ReleaseNotes -ceq $ReleaseNotes.Trim() -and
        $previous.Standard.Sha256 -ceq $releaseData['Standard'].Sha256 -and
        $previous.Auto.Sha256 -ceq $releaseData['Auto'].Sha256
    if ($version -le [Version]$previous.Version -and !($ReSignCurrentVersion -and $sameRelease)) {
        throw "Update version $version must exceed the existing manifest version."
    }
}

$manifest = [ordered]@{
    SchemaVersion = 2
    ProductId = 'Gyeon-ai/MyGomiPinball'
    Version = $version.ToString()
    ReleaseNotes = $ReleaseNotes.Trim()
    Standard = $releaseData['Standard']
    Auto = $releaseData['Auto']
}
$manifestJson = ($manifest | ConvertTo-Json -Depth 4).Replace("`r`n", "`n").Replace("`r", "`n")
$manifestBytes = $utf8NoBom.GetBytes($manifestJson + "`n")
$protectedBytes = [IO.File]::ReadAllBytes($SigningKeyPath)
$plainBytes = [Security.Cryptography.ProtectedData]::Unprotect(
    $protectedBytes, $entropy, [Security.Cryptography.DataProtectionScope]::CurrentUser)
$privateXml = $null
try {
    $privateXml = [Text.Encoding]::UTF8.GetString($plainBytes)
}
finally {
    [Array]::Clear($plainBytes, 0, $plainBytes.Length)
}

function New-RsaProvider {
    $parameters = New-Object Security.Cryptography.CspParameters
    $parameters.ProviderType = 24
    $provider = New-Object Security.Cryptography.RSACryptoServiceProvider($parameters)
    $provider.PersistKeyInCsp = $false
    return $provider
}

$oid = [Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256')
$rsa = New-RsaProvider
try {
    $rsa.FromXmlString($privateXml)
    $signatureBytes = $rsa.SignData($manifestBytes, $oid)
}
finally {
    $rsa.Dispose()
    $privateXml = $null
}
$publicRsa = New-RsaProvider
try {
    $publicRsa.FromXmlString([IO.File]::ReadAllText($publicKeyPath))
    if (!$publicRsa.VerifyData($manifestBytes, $oid, $signatureBytes)) {
        throw 'Signing key does not match the embedded public key.'
    }
}
finally {
    $publicRsa.Dispose()
}

$backupDirectory = Join-Path ([IO.Path]::GetTempPath()) ('MyGomiRelease-' + [Guid]::NewGuid().ToString('N'))
$paths = @($outputs | ForEach-Object { $_.Destination }) + @($manifestPath, $signaturePath)
$existed = @{}
New-Item -ItemType Directory -Path $backupDirectory | Out-Null
$success = $false
$mutated = $false
try {
    for ($index = 0; $index -lt $paths.Count; $index++) {
        $existed[$index] = Test-Path -LiteralPath $paths[$index]
        if ($existed[$index]) {
            Copy-Item -LiteralPath $paths[$index] -Destination (Join-Path $backupDirectory "$index.bak")
        }
    }
    $mutated = $true
    foreach ($output in $outputs) {
        Copy-Item -LiteralPath $output.Source -Destination $output.Destination -Force
    }
    [IO.File]::WriteAllBytes($manifestPath, $manifestBytes)
    [IO.File]::WriteAllText($signaturePath, [Convert]::ToBase64String($signatureBytes) + "`n", $utf8NoBom)
    & (Join-Path $PSScriptRoot 'Test-UpdateSignature.ps1') -ManifestPath $manifestPath -SignaturePath $signaturePath -PublicKeyPath $publicKeyPath
    foreach ($output in $outputs) {
        if ((Get-FileHash -Algorithm SHA256 -LiteralPath $output.Source).Hash -ne
            (Get-FileHash -Algorithm SHA256 -LiteralPath $output.Destination).Hash) {
            throw "Published alias hash mismatch: $($output.Destination)"
        }
    }
    $success = $true
    Write-Host "Prepared signed MyGomi update $version."
    foreach ($project in $projects) {
        Write-Host "$($project.Key): $($releaseData[$project.Key].Sha256)"
    }
}
finally {
    if (!$success -and $mutated) {
        for ($index = 0; $index -lt $paths.Count; $index++) {
            if ($existed[$index]) {
                Copy-Item -LiteralPath (Join-Path $backupDirectory "$index.bak") -Destination $paths[$index] -Force
            }
            elseif (Test-Path -LiteralPath $paths[$index]) {
                Remove-Item -LiteralPath $paths[$index] -Force
            }
        }
    }
    $safeTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $resolvedBackup = [IO.Path]::GetFullPath($backupDirectory)
    if (!$resolvedBackup.StartsWith($safeTemp, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedBackup) -notmatch '^MyGomiRelease-[a-f0-9]{32}$') {
        throw 'Unsafe release backup cleanup path.'
    }
    Remove-Item -LiteralPath $resolvedBackup -Recurse -Force
}
