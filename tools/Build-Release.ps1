param(
    [ValidateSet("MyGomiPinball", "MyGomiPinballAuto", "All")]
    [string[]]$Project = @("All"),

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$MsBuildPath
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$publisher = Join-Path $PSScriptRoot "Publish-NumberedBuild.ps1"
$outputRoot = Join-Path $root "dist"
$knownProjects = @("MyGomiPinball", "MyGomiPinballAuto")

function Resolve-MsBuild {
    param([string]$RequestedPath)

    if (![String]::IsNullOrWhiteSpace($RequestedPath)) {
        if (Test-Path -LiteralPath $RequestedPath) {
            return (Resolve-Path -LiteralPath $RequestedPath).Path
        }

        throw "MSBuild was not found: $RequestedPath"
    }

    $vswhereCandidates = @(
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\Installer\vswhere.exe"
    )

    foreach ($vswhere in $vswhereCandidates) {
        if ([String]::IsNullOrWhiteSpace($vswhere) -or !(Test-Path -LiteralPath $vswhere)) {
            continue
        }

        $discovered = @(
            & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" 2>$null
        ) | Select-Object -First 1
        if (![String]::IsNullOrWhiteSpace($discovered) -and (Test-Path -LiteralPath $discovered)) {
            return (Resolve-Path -LiteralPath $discovered).Path
        }
    }

    $candidates = @(
        "${env:ProgramFiles}\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2026\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2026\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2026\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program1\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program\MSBuild\Current\Bin\MSBuild.exe"
    )

    foreach ($candidate in $candidates) {
        if (![String]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    $command = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($command -ne $null) {
        return $command.Source
    }

    throw "MSBuild.exe was not found. Pass -MsBuildPath or install Visual Studio Build Tools."
}
if (!(Test-Path -LiteralPath $publisher)) {
    throw "Numbered artifact publisher was not found: $publisher"
}

if ($Project -contains "All") {
    $selectedProjects = $knownProjects
}
else {
    $selectedProjects = $Project
}

$msbuild = Resolve-MsBuild $MsBuildPath
Write-Host "MSBuild: $msbuild"

foreach ($projectName in $selectedProjects) {
    $projectFile = Join-Path (Join-Path $root $projectName) "$projectName.csproj"
    if (!(Test-Path -LiteralPath $projectFile)) {
        throw "Project file was not found: $projectFile"
    }

    Write-Host "Rebuilding $projectName ($Configuration)..."
    & $msbuild $projectFile /t:Rebuild /p:Configuration=$Configuration /p:Platform=AnyCPU /nologo /v:m
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild failed for $projectName with exit code $LASTEXITCODE."
    }

    $sourceDir = Join-Path (Join-Path (Join-Path $root "build") $Configuration) $projectName
    $sourceExe = Join-Path $sourceDir "$projectName.exe"
    $sourcePdb = Join-Path $sourceDir "$projectName.pdb"

    & $publisher -ProjectName $projectName -Configuration $Configuration -SourceExe $sourceExe -SourcePdb $sourcePdb -OutputRoot $outputRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Numbered artifact publishing failed for $projectName with exit code $LASTEXITCODE."
    }
}

