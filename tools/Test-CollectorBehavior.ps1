param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [Parameter(Mandatory = $true)][string]$NamespaceName,
    [Parameter(Mandatory = $true)][string]$ExpectedVersion,
    [switch]$ExpectAuto
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
$flags = [Reflection.BindingFlags]'Instance,Public,NonPublic'
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $ExePath).Path)
$type = $assembly.GetType("$NamespaceName.MainForm", $true)
$form = [Activator]::CreateInstance($type, $flags, $null, @(), $null)

function Assert-Equal($Expected, $Actual, [string]$Message) {
    if ($Expected -ne $Actual) { throw "$Message (expected=$Expected, actual=$Actual)" }
}
function Field([string]$Name) { return ,$type.GetField($Name, $flags).GetValue($form) }
function Set-Field([string]$Name, $Value) { $type.GetField($Name, $flags).SetValue($form, $Value) }
function Invoke-Form([string]$Name, [object[]]$Arguments) {
    return $type.GetMethod($Name, $flags).Invoke($form, $Arguments)
}

try {
    $expectedTitle = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('6rOw7J20IOyiheqynO2VgOuzvA=='))
    Assert-Equal $ExpectedVersion $assembly.GetName().Version.ToString() 'Assembly version'
    Assert-Equal $ExpectedVersion ([Diagnostics.FileVersionInfo]::GetVersionInfo($ExePath).FileVersion) 'File version'
    Assert-Equal $expectedTitle $form.Text 'Window title'
    Assert-Equal $expectedTitle (Field '_appTitle').Text 'Visible program title'
    Assert-Equal $expectedTitle ([Diagnostics.FileVersionInfo]::GetVersionInfo($ExePath).ProductName) 'Windows product name'
    Assert-Equal ([bool]$ExpectAuto) ($null -ne $assembly.GetType("$NamespaceName.PinballSiteInjector", $false)) 'General/Auto separation'
    $entries = Field '_entries'
    $pending = Field '_pending'
    $sourceType = $assembly.GetType("$NamespaceName.GiftSource", $true)
    $threshold = Field '_thresholdInput'
    $threshold.Value = 200
    Set-Field '_exactMode' $true
    Set-Field '_nicknamePinballMode' $true

    # Exercise the real gift handlers without opening a broadcast connection.
    foreach ($sourceName in 'StarBalloon','AdBalloon','ChallengeGift') {
        $source = [Enum]::Parse($sourceType, $sourceName)
        foreach ($count in 100,300) {
            $before = $entries.Count
            Invoke-Form 'HandleBalloonGift' @($source, 'rejected', $count) | Out-Null
            Assert-Equal $before $entries.Count "$sourceName non-multiple rejected"
        }
        foreach ($case in @(@(200,1),@(400,2),@(2000,10),@(4000,20))) {
            $before = $entries.Count
            Invoke-Form 'HandleBalloonGift' @($source, 'nickname-test', $case[0]) | Out-Null
            Assert-Equal ($before + 1) $entries.Count "$sourceName multiple collected"
            Assert-Equal $case[1] $entries[$entries.Count - 1].CoinCount "$sourceName coins"
            Assert-Equal 'nickname-test' $entries[$entries.Count - 1].PinballName "$sourceName nickname mode"
        }
        $enabledField = '_collect' + $sourceName
        Set-Field $enabledField $false
        $before = $entries.Count
        Invoke-Form 'HandleBalloonGift' @($source, 'disabled', 400) | Out-Null
        Assert-Equal $before $entries.Count "$sourceName disabled source"
        Set-Field $enabledField $true

        Set-Field '_nicknamePinballMode' $false
        $before = $entries.Count
        Invoke-Form 'HandleBalloonGift' @($source, 'same-user', 200) | Out-Null
        Invoke-Form 'HandleBalloonGift' @($source, 'same-user', 400) | Out-Null
        Assert-Equal $before $entries.Count "$sourceName wait for chat"
        Assert-Equal 2 $pending.Count "$sourceName pending queue"
        Invoke-Form 'HandleChatMessage' @('different-user', 'ignored') | Out-Null
        Assert-Equal 2 $pending.Count "$sourceName unmatched chat"
        Invoke-Form 'HandleChatMessage' @('same-user', 'first-message') | Out-Null
        Assert-Equal 1 $entries[$entries.Count - 1].CoinCount "$sourceName FIFO first"
        Invoke-Form 'HandleChatMessage' @('same-user', 'second-message') | Out-Null
        Assert-Equal 2 $entries[$entries.Count - 1].CoinCount "$sourceName FIFO second"
        Assert-Equal 'second-message' $entries[$entries.Count - 1].PinballName "$sourceName content mode"
        Assert-Equal 0 $pending.Count "$sourceName consumed queue"
        $before = $entries.Count
        Invoke-Form 'HandleChatMessage' @('same-user', 'duplicate') | Out-Null
        Assert-Equal $before $entries.Count "$sourceName one chat per gift"

        Invoke-Form 'HandleBalloonGift' @($source, 'expired-user', 200) | Out-Null
        $pending[0].CreatedAt = [DateTime]::Now.AddMinutes(-11)
        Invoke-Form 'HandleChatMessage' @('expired-user', 'too-late') | Out-Null
        Assert-Equal $before $entries.Count "$sourceName expired gift"
        Assert-Equal 0 $pending.Count "$sourceName expired queue removed"
        Set-Field '_nicknamePinballMode' $true
        Set-Field '_exactMode' $false
        Invoke-Form 'HandleBalloonGift' @($source, 'at-least', 300) | Out-Null
        Assert-Equal ($before + 1) $entries.Count "$sourceName at-least mode"
        Assert-Equal 1 $entries[$entries.Count - 1].CoinCount "$sourceName at-least coins"
        Invoke-Form 'HandleBalloonGift' @($source, 'at-least-ten', 2100) | Out-Null
        Assert-Equal 10 $entries[$entries.Count - 1].CoinCount "$sourceName at-least ten coins without bonus"
        Set-Field '_exactMode' $true

        Set-Field '_nicknamePinballMode' $false
        $before = $entries.Count
        Invoke-Form 'HandleBalloonGift' @($source, 'ten-unit-chat', 2000) | Out-Null
        Assert-Equal 1 $pending.Count "$sourceName ten-unit gift pending"
        Invoke-Form 'HandleChatMessage' @('ten-unit-chat', 'ten-unit-content') | Out-Null
        Assert-Equal ($before + 1) $entries.Count "$sourceName ten-unit chat collected"
        Assert-Equal 10 $entries[$entries.Count - 1].CoinCount "$sourceName ten-unit chat without bonus"
        Assert-Equal 0 $pending.Count "$sourceName ten-unit gift consumed"
        Set-Field '_nicknamePinballMode' $true
    }

    $threshold.Value = 100
    Invoke-Form 'RecalculateCoins' @() | Out-Null
    Assert-Equal 20 $entries[$entries.Count - 1].CoinCount 'Threshold change recalculates chat entry without bonus'
    $guideLines = (Invoke-Form 'BuildCoinGuideText' @()) -split "`r`n"
    Assert-Equal 2 $guideLines.Count 'Coin guide line count'
    Assert-Equal $true ($guideLines[0] -match '100.*= 1') 'Coin guide base rate'
    Assert-Equal $true ($guideLines[1] -match '^1000.*= 10') 'Coin guide no-bonus rate'
    $source = [Enum]::Parse($sourceType, 'StarBalloon')
    foreach ($case in @(@(1000,10),@(2000,20))) {
        Invoke-Form 'HandleBalloonGift' @($source, 'hundred-unit', $case[0]) | Out-Null
        Assert-Equal $case[1] $entries[$entries.Count - 1].CoinCount "100-balloon unit $($case[0]) coins"
    }
    Write-Output "PASS: $($assembly.GetName().Name) $ExpectedVersion; 3 gift sources, exact/at-least multiples, no bonus, guide, filters, nickname/chat, FIFO, expiry"
}
finally { $form.Dispose() }
