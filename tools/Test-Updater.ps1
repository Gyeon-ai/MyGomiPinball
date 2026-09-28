param(
    [Parameter(Mandatory = $true)]
    [string]$RepositoryRoot
)

$ErrorActionPreference = 'Stop'
$flags = [Reflection.BindingFlags]'Static,Instance,Public,NonPublic'

function Assert-True([bool]$Condition, [string]$Message) {
    if (!$Condition) {
        throw $Message
    }
}

function Assert-Equal($Expected, $Actual, [string]$Message) {
    if ($Expected -ne $Actual) {
        throw "$Message (expected: $Expected, actual: $Actual)"
    }
}

function Invoke-Method($Method, $Target, [object[]]$Arguments) {
    $normalizedArguments = New-Object object[] $Arguments.Length
    for ($index = 0; $index -lt $Arguments.Length; $index++) {
        $argument = $Arguments[$index]
        if ($argument -is [Management.Automation.PSObject]) {
            $normalizedArguments[$index] = $argument.PSObject.BaseObject
        }
        else {
            $normalizedArguments[$index] = $argument
        }
    }

    try {
        return $Method.Invoke($Target, $normalizedArguments)
    }
    catch [Reflection.TargetInvocationException] {
        if ($null -ne $_.Exception.InnerException) {
            throw $_.Exception.InnerException
        }
        throw
    }
    catch {
        throw "Reflection call failed for $($Method.Name): $($_.Exception.Message)"
    }
}

function Assert-Throws([scriptblock]$Action, [string]$Message) {
    try {
        & $Action
    }
    catch {
        return
    }
    throw $Message
}

function Get-ControlTexts($Control) {
    $texts = New-Object Collections.Generic.List[string]
    foreach ($child in $Control.Controls) {
        if (![String]::IsNullOrWhiteSpace($child.Text)) {
            $texts.Add($child.Text)
        }
        foreach ($nested in Get-ControlTexts $child) {
            $texts.Add($nested)
        }
    }
    return $texts
}

$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$manifestPath = Join-Path $root 'update.json'
$signaturePath = Join-Path $root 'update.json.sig'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
Assert-Equal 2 $manifest.SchemaVersion 'The manifest schema version is wrong'
$expectedVersion = [Reflection.AssemblyName]::GetAssemblyName(
    (Join-Path $root 'build\Release\MyGomiPinball\MyGomiPinball.exe')).Version.ToString()
Assert-Equal $expectedVersion $manifest.Version 'The manifest version does not match the build'
$manifestBytes = [IO.File]::ReadAllBytes($manifestPath)
$signatureDocumentBytes = [IO.File]::ReadAllBytes($signaturePath)

$products = @(
    [pscustomobject]@{
        Name = 'standard'
        AssemblyName = 'MyGomiPinball'
        BuildExe = Join-Path $root 'build\Release\MyGomiPinball\MyGomiPinball.exe'
        CurrentExe = Join-Path $root 'dist\Release\MyGomiPinball\MyGomiPinball-009.exe'
        OtherBuildExe = Join-Path $root 'build\Release\MyGomiPinballAuto\MyGomiPinballAuto.exe'
    },
    [pscustomobject]@{
        Name = 'auto'
        AssemblyName = 'MyGomiPinballAuto'
        BuildExe = Join-Path $root 'build\Release\MyGomiPinballAuto\MyGomiPinballAuto.exe'
        CurrentExe = Join-Path $root 'dist\Release\MyGomiPinballAuto\MyGomiPinballAuto-009.exe'
        OtherBuildExe = Join-Path $root 'build\Release\MyGomiPinball\MyGomiPinball.exe'
    }
)

foreach ($product in $products) {
    $assembly = [Reflection.Assembly]::LoadFrom($product.BuildExe)
    $updaterType = $assembly.GetType($product.AssemblyName + '.SelfUpdater', $true)
    $promptType = $assembly.GetType($product.AssemblyName + '.UpdatePromptDialog', $true)

    $computeHash = $updaterType.GetMethod('ComputeSha256', $flags)
    $isValidHash = $updaterType.GetMethod('IsValidSha256', $flags)
    $isAllowedHost = $updaterType.GetMethod('IsAllowedDownloadHost', $flags)
    $validateIdentity = $updaterType.GetMethod('ValidateAssemblyIdentity', $flags)
    $replaceExecutable = $updaterType.GetMethod('ReplaceExecutable', $flags)
    $restoreBackup = $updaterType.GetMethod('RestoreBackup', $flags)
    $signalStartup = $updaterType.GetMethod('SignalSuccessfulStartup', $flags)
    $verifyManifestSignature = $updaterType.GetMethod('VerifyManifestSignature', $flags)

    foreach ($method in @($computeHash, $isValidHash, $isAllowedHost, $validateIdentity, $replaceExecutable, $restoreBackup, $signalStartup, $verifyManifestSignature)) {
        Assert-True ($null -ne $method) "Updater method was not found in $($product.Name)"
    }

    $signatureArguments = New-Object object[] 2
    $signatureArguments[0] = $manifestBytes
    $signatureArguments[1] = $signatureDocumentBytes
    Invoke-Method $verifyManifestSignature $null $signatureArguments | Out-Null

    $tamperedManifestBytes = New-Object byte[] $manifestBytes.Length
    [Array]::Copy($manifestBytes, $tamperedManifestBytes, $manifestBytes.Length)
    $tamperedManifestBytes[0] = $tamperedManifestBytes[0] -bxor 1
    $tamperedArguments = New-Object object[] 2
    $tamperedArguments[0] = $tamperedManifestBytes
    $tamperedArguments[1] = $signatureDocumentBytes
    Assert-Throws {
        Invoke-Method $verifyManifestSignature $null $tamperedArguments | Out-Null
    } "A tampered update manifest was accepted in $($product.Name)"

    $sourceHash = Invoke-Method $computeHash $null @($product.BuildExe)
    Assert-True (Invoke-Method $isValidHash $null @($sourceHash)) "A valid SHA256 was rejected in $($product.Name)"
    Assert-True (!(Invoke-Method $isValidHash $null @('0' * 63))) "An invalid SHA256 was accepted in $($product.Name)"
    Assert-True (Invoke-Method $isAllowedHost $null @('github.com')) "github.com was rejected in $($product.Name)"
    Assert-True (!(Invoke-Method $isAllowedHost $null @('example.invalid'))) "An untrusted host was accepted in $($product.Name)"

    $builtVersion = [Reflection.AssemblyName]::GetAssemblyName($product.BuildExe).Version
    $identity = Invoke-Method $validateIdentity $null @($product.BuildExe, $product.AssemblyName, $builtVersion)
    Assert-Equal $product.AssemblyName $identity.Name "The product identity is wrong in $($product.Name)"
    Assert-Throws {
        Invoke-Method $validateIdentity $null @($product.OtherBuildExe, $product.AssemblyName, $null)
    } "A cross-product update was accepted in $($product.Name)"

    $testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('MyGomiUpdaterAudit-' + $product.Name + '-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $testDirectory | Out-Null
    try {
        $targetPath = Join-Path $testDirectory ($product.AssemblyName + '.exe')
        Copy-Item -LiteralPath $product.CurrentExe -Destination $targetPath
        $oldHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $targetPath).Hash
        $backupPath = Invoke-Method $replaceExecutable $null @($product.BuildExe, $targetPath, $sourceHash)
        Assert-Equal $sourceHash (Get-FileHash -Algorithm SHA256 -LiteralPath $targetPath).Hash "Atomic replacement installed the wrong file in $($product.Name)"
        Assert-Equal $oldHash (Get-FileHash -Algorithm SHA256 -LiteralPath $backupPath).Hash "Atomic replacement did not preserve the old file in $($product.Name)"
        Invoke-Method $restoreBackup $null @($targetPath, $backupPath) | Out-Null
        Assert-Equal $oldHash (Get-FileHash -Algorithm SHA256 -LiteralPath $targetPath).Hash "Rollback did not restore the old file in $($product.Name)"
        Assert-True (!(Test-Path -LiteralPath $backupPath)) "Rollback left the backup file behind in $($product.Name)"

        Copy-Item -LiteralPath $product.CurrentExe -Destination $targetPath -Force
        $beforeRejectedUpdate = (Get-FileHash -Algorithm SHA256 -LiteralPath $targetPath).Hash
        Assert-Throws {
            Invoke-Method $replaceExecutable $null @($product.BuildExe, $targetPath, ('0' * 64))
        } "A bad update hash was accepted in $($product.Name)"
        Assert-Equal $beforeRejectedUpdate (Get-FileHash -Algorithm SHA256 -LiteralPath $targetPath).Hash "A rejected update changed the target in $($product.Name)"

        $token = [Guid]::NewGuid().ToString('N')
        $signalPath = Join-Path ([IO.Path]::GetTempPath()) ("MyGomiPinballUpdate-$token.signal")
        $encodedSignalPath = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($signalPath))
        $signalArgs = [string[]]@("--update-signal=$encodedSignalPath", "--update-token=$token")
        Invoke-Method $signalStartup $null ([object[]](,$signalArgs)) | Out-Null
        Assert-True (Test-Path -LiteralPath $signalPath) "The startup signal was not created in $($product.Name)"
        Assert-Equal $token ([IO.File]::ReadAllText($signalPath).Trim()) "The startup signal token is wrong in $($product.Name)"
        Remove-Item -LiteralPath $signalPath -Force

        $blockedSignalPath = Join-Path $testDirectory 'MyGomiPinballUpdate-blocked.signal'
        $encodedBlockedPath = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($blockedSignalPath))
        $blockedArgs = [string[]]@("--update-signal=$encodedBlockedPath", '--update-token=blocked')
        Invoke-Method $signalStartup $null ([object[]](,$blockedArgs)) | Out-Null
        Assert-True (!(Test-Path -LiteralPath $blockedSignalPath)) "A startup signal escaped the temp directory in $($product.Name)"
    }
    finally {
        if (Test-Path -LiteralPath $testDirectory) {
            $resolvedTestDirectory = [IO.Path]::GetFullPath($testDirectory)
            $safeTemporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
            if (!$resolvedTestDirectory.StartsWith($safeTemporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
                [IO.Path]::GetFileName($resolvedTestDirectory) -notmatch '^MyGomiUpdaterAudit-(standard|auto)-[a-f0-9]{32}$') {
                throw 'Unsafe updater audit cleanup path.'
            }
            Remove-Item -LiteralPath $resolvedTestDirectory -Recurse -Force
        }
    }

    $prompt = [Activator]::CreateInstance(
        $promptType,
        $flags,
        $null,
        [object[]]@([Version]'9.9.9.9', 'test release'),
        $null)
    try {
        $controlTexts = @(Get-ControlTexts $prompt)
        $updateText = -join @([char]0xC5C5, [char]0xB370, [char]0xC774, [char]0xD2B8)
        $laterText = -join @([char]0xB098, [char]0xC911, [char]0xC5D0)
        Assert-True ($controlTexts -contains $updateText) "The update button is missing in $($product.Name)"
        Assert-True ($controlTexts -contains $laterText) "The later button is missing in $($product.Name)"
    }
    finally {
        $prompt.Dispose()
    }

    Write-Output "PASS: $($product.AssemblyName) updater validation"
}
