$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$publisher = Join-Path $PSScriptRoot "Publish-NumberedBuild.ps1"
$recordedState = Get-Content -LiteralPath (Join-Path $PSScriptRoot "ReleaseState.json") -Raw | ConvertFrom-Json
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("MyGomiReleaseNumbering-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $testRoot | Out-Null

try {
    foreach ($project in @("MyGomiPinball", "MyGomiPinballAuto")) {
        $sourceExe = Join-Path $root "$project.exe"
        $statePath = Join-Path $testRoot "$project.json"
        $outputRoot = Join-Path $testRoot $project
        $state = $recordedState | ConvertTo-Json -Depth 4 | ConvertFrom-Json
        $state.PSObject.Properties[$project].Value.Version = "0.0.0.0"
        $state | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $statePath -Encoding UTF8

        & $publisher -ProjectName $project -Configuration Release -SourceExe $sourceExe -OutputRoot $outputRoot -ReleaseStatePath $statePath
        $expectedNumber = [int]$recordedState.PSObject.Properties[$project].Value.Number + 1
        $expectedExe = Join-Path (Join-Path (Join-Path $outputRoot "Release") $project) ("{0}-{1:000}.exe" -f $project, $expectedNumber)
        if (!(Test-Path -LiteralPath $expectedExe)) {
            throw "Fresh checkout did not continue at number $expectedNumber for $project."
        }

        $updated = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        if ([int]$updated.PSObject.Properties[$project].Value.Number -ne $expectedNumber) {
            throw "Release state was not updated for $project."
        }

        $rejected = $false
        try {
            & $publisher -ProjectName $project -Configuration Release -SourceExe $sourceExe -OutputRoot $outputRoot -ReleaseStatePath $statePath
        }
        catch {
            $rejected = $_.Exception.Message -like "Bump $project version*"
        }
        if (!$rejected) {
            throw "Publishing the same version twice was not rejected for $project."
        }
    }

    Write-Host "Release numbering: both projects passed."
}
finally {
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $resolvedTest = [IO.Path]::GetFullPath($testRoot)
    if (!$resolvedTest.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a path outside the temporary directory: $resolvedTest"
    }
    Remove-Item -LiteralPath $resolvedTest -Recurse -Force
}
