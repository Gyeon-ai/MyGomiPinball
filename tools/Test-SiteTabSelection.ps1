param(
    [Parameter(Mandatory = $true)][string]$ExePath
)

$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $ExePath).Path)
$type = $assembly.GetType('MyGomiPinballAuto.PinballSiteInjector', $true)
$flags = [Reflection.BindingFlags]'Static,NonPublic'
$method = $type.GetMethod('ExtractWebSocketDebuggerUrl', $flags)
if ($null -eq $method) { throw 'Site tab selection method was not found.' }

$tabs = '[{"url":"chrome://newtab/","webSocketDebuggerUrl":"ws://wrong"},{"url":"https://gyeon-ai.github.io/MyGomiPinball-Web/?names=test","webSocketDebuggerUrl":"ws://right"}]'
$selected = $method.Invoke($null, @($tabs, 'https://gyeon-ai.github.io/MyGomiPinball-Web/'))
if ($selected -ne 'ws://right') { throw "Expected the matching site tab, got: $selected" }

$unrelated = '[{"url":"https://gyeon-ai.github.io/TayoPinball-Web/","webSocketDebuggerUrl":"ws://wrong"}]'
$selected = $method.Invoke($null, @($unrelated, 'https://gyeon-ai.github.io/MyGomiPinball-Web/'))
if ($selected -ne '') { throw "An unrelated tab was selected: $selected" }

Write-Output 'PASS: matching site tab selected; unrelated tab rejected'
