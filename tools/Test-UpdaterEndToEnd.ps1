param(
    [Parameter(Mandatory = $true)]
    [string]$RepositoryRoot
)

$ErrorActionPreference = 'Stop'

function Assert-True([bool]$Condition, [string]$Message) {
    if (!$Condition) {
        throw $Message
    }
}

function Get-ProcessByExecutablePath([string]$ExecutablePath) {
    $expected = [IO.Path]::GetFullPath($ExecutablePath)
    foreach ($process in Get-Process) {
        try {
            if ([String]::Equals([IO.Path]::GetFullPath($process.Path), $expected, [StringComparison]::OrdinalIgnoreCase)) {
                return $process
            }
        }
        catch {
        }
    }
    return $null
}

$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$auditRoot = Join-Path ([IO.Path]::GetTempPath()) 'MyGomiPinballUpdaterE2E'
New-Item -ItemType Directory -Force -Path $auditRoot | Out-Null
$products = @(
    [pscustomobject]@{
        Name = 'standard'
        AssemblyName = 'MyGomiPinball'
        BuildExe = Join-Path $root 'build\Release\MyGomiPinball\MyGomiPinball.exe'
        CurrentExe = Join-Path $root 'dist\Release\MyGomiPinball\MyGomiPinball-009.exe'
    },
    [pscustomobject]@{
        Name = 'auto'
        AssemblyName = 'MyGomiPinballAuto'
        BuildExe = Join-Path $root 'build\Release\MyGomiPinballAuto\MyGomiPinballAuto.exe'
        CurrentExe = Join-Path $root 'dist\Release\MyGomiPinballAuto\MyGomiPinballAuto-009.exe'
    }
)

foreach ($product in $products) {
    $testDirectory = Join-Path $auditRoot ('apply-e2e-' + $product.Name + '-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $testDirectory | Out-Null
    $targetPath = Join-Path $testDirectory ($product.AssemblyName + '.exe')
    $updaterPath = Join-Path $testDirectory ('downloaded-' + $product.AssemblyName + '.exe')
    $targetProcess = $null
    $updaterProcess = $null

    try {
        Copy-Item -LiteralPath $product.CurrentExe -Destination $targetPath
        Copy-Item -LiteralPath $product.BuildExe -Destination $updaterPath
        $expectedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $updaterPath).Hash
        $encodedTarget = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($targetPath))
        $arguments = "--apply-update=$encodedTarget --wait-pid=0 --expected-sha256=$expectedHash"

        $updaterProcess = Start-Process -FilePath $updaterPath -ArgumentList $arguments -PassThru -WindowStyle Hidden
        Assert-True ($updaterProcess.WaitForExit(30000)) "The updater process timed out for $($product.Name)"
        Assert-True ((Test-Path -LiteralPath $targetPath)) "The target disappeared for $($product.Name)"
        Assert-True (((Get-FileHash -Algorithm SHA256 -LiteralPath $targetPath).Hash -eq $expectedHash)) "The end-to-end update installed the wrong file for $($product.Name)"
        Assert-True (!(Test-Path -LiteralPath ($targetPath + '.update-new'))) "The staging file remained for $($product.Name)"
        Assert-True (!(Test-Path -LiteralPath ($targetPath + '.update-backup'))) "The backup was not finalized for $($product.Name)"

        for ($attempt = 0; $attempt -lt 30 -and $null -eq $targetProcess; $attempt++) {
            Start-Sleep -Milliseconds 200
            $targetProcess = Get-ProcessByExecutablePath $targetPath
        }
        Assert-True ($null -ne $targetProcess) "The updated application did not remain running for $($product.Name)"

        Write-Output "PASS: $($product.AssemblyName) end-to-end self-update"
    }
    finally {
        if ($null -ne $targetProcess) {
            try {
                if (!$targetProcess.HasExited) {
                    $targetProcess.Kill()
                    $targetProcess.WaitForExit(5000) | Out-Null
                }
            }
            catch {
            }
            $targetProcess.Dispose()
        }
        if ($null -ne $updaterProcess) {
            try {
                if (!$updaterProcess.HasExited) {
                    $updaterProcess.Kill()
                    $updaterProcess.WaitForExit(5000) | Out-Null
                }
            }
            catch {
            }
            $updaterProcess.Dispose()
        }

        if (Test-Path -LiteralPath $testDirectory) {
            $resolvedTestDirectory = (Resolve-Path -LiteralPath $testDirectory).Path
            $resolvedAuditRoot = (Resolve-Path -LiteralPath $auditRoot).Path
            if (!$resolvedTestDirectory.StartsWith($resolvedAuditRoot, [StringComparison]::OrdinalIgnoreCase)) {
                throw 'Unsafe updater test cleanup path.'
            }
            $cleanupError = $null
            for ($attempt = 0; $attempt -lt 20; $attempt++) {
                try {
                    Remove-Item -LiteralPath $resolvedTestDirectory -Recurse -Force -ErrorAction Stop
                    $cleanupError = $null
                    break
                }
                catch {
                    $cleanupError = $_
                    Start-Sleep -Milliseconds 250
                }
            }
            if ($null -ne $cleanupError) {
                throw $cleanupError
            }
        }
    }
}
