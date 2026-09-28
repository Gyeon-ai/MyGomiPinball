param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectName,

    [Parameter(Mandatory = $true)]
    [string]$Configuration,

    [Parameter(Mandatory = $true)]
    [string]$SourceExe,

    [string]$SourcePdb,

    [Parameter(Mandatory = $true)]
    [string]$OutputRoot
)

$ErrorActionPreference = "Stop"

if (!(Test-Path -LiteralPath $SourceExe)) {
    throw "Source executable was not found: $SourceExe"
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
do {
    $suffix = $nextNumber.ToString("000")
    $targetExe = Join-Path $outputDir "$ProjectName-$suffix.exe"
    $targetPdb = Join-Path $outputDir "$ProjectName-$suffix.pdb"
    $nextNumber++
} while (Test-Path -LiteralPath $targetExe)

Copy-Item -LiteralPath $SourceExe -Destination $targetExe

if (![String]::IsNullOrWhiteSpace($SourcePdb) -and (Test-Path -LiteralPath $SourcePdb)) {
    Copy-Item -LiteralPath $SourcePdb -Destination $targetPdb
}

Write-Host "Numbered artifact: $targetExe"
