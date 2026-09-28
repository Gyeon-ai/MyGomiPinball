param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectName,

    [Parameter(Mandatory = $true)]
    [string]$Configuration,

    [Parameter(Mandatory = $true)]
    [string]$SourceExe,

    [string]$SourcePdb,

    [Parameter(Mandatory = $true)]
    [string]$OutputRoot,

    [string]$ReleaseStatePath = (Join-Path $PSScriptRoot "ReleaseState.json")
)

$ErrorActionPreference = "Stop"

if (!(Test-Path -LiteralPath $SourceExe)) {
    throw "Source executable was not found: $SourceExe"
}

$releaseState = $null
$projectState = $null
if ($Configuration -eq "Release") {
    if (!(Test-Path -LiteralPath $ReleaseStatePath)) {
        throw "Release state was not found: $ReleaseStatePath"
    }

    $releaseState = Get-Content -LiteralPath $ReleaseStatePath -Raw | ConvertFrom-Json
    $projectProperty = $releaseState.PSObject.Properties[$ProjectName]
    if ($null -eq $projectProperty) {
        throw "Release state has no entry for $ProjectName."
    }

    $projectState = $projectProperty.Value
    $sourceVersionText = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($SourceExe).FileVersion
    $sourceVersion = $null
    $recordedVersion = $null
    if (![Version]::TryParse($sourceVersionText, [ref]$sourceVersion) -or
        ![Version]::TryParse([string]$projectState.Version, [ref]$recordedVersion)) {
        throw "The source or recorded release version is invalid for $ProjectName."
    }

    if ($sourceVersion -le $recordedVersion) {
        throw "Bump $ProjectName version above $recordedVersion before publishing another release."
    }

    if ([int]$projectState.Number -lt 0) {
        throw "The recorded release number is invalid for $ProjectName."
    }
}

$outputDir = Join-Path (Join-Path $OutputRoot $Configuration) $ProjectName
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

$escapedName = [Regex]::Escape($ProjectName)
$maxNumber = 0

Get-ChildItem -LiteralPath $outputDir -Filter "$ProjectName-*.exe" -File -ErrorAction SilentlyContinue | ForEach-Object {
    if ($_.BaseName -match "^$escapedName-([0-9]+)$") {
        $number = [int]$Matches[1]
        if ($number -gt $maxNumber) {
            $maxNumber = $number
        }
    }
}

$nextNumber = $maxNumber + 1
if ($null -ne $projectState) {
    $nextNumber = [Math]::Max($nextNumber, [int]$projectState.Number + 1)
}
do {
    $suffix = $nextNumber.ToString("000")
    $targetExe = Join-Path $outputDir "$ProjectName-$suffix.exe"
    $targetPdb = Join-Path $outputDir "$ProjectName-$suffix.pdb"
    $nextNumber++
} while (Test-Path -LiteralPath $targetExe)

$publishedNumber = $nextNumber - 1
Copy-Item -LiteralPath $SourceExe -Destination $targetExe

if (![String]::IsNullOrWhiteSpace($SourcePdb) -and (Test-Path -LiteralPath $SourcePdb)) {
    Copy-Item -LiteralPath $SourcePdb -Destination $targetPdb
}

if ($null -ne $projectState) {
    $projectState.Version = $sourceVersion.ToString()
    $projectState.Number = $publishedNumber
    $releaseState | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $ReleaseStatePath -Encoding UTF8
}

Write-Host "Numbered artifact: $targetExe"
