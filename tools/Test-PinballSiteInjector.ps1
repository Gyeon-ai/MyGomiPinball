param(
    [Parameter(Mandatory = $true)][string]$ExePath
)

$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $ExePath).Path)
$type = $assembly.GetType('MyGomiPinballAuto.PinballSiteInjector', $true)
$flags = [Reflection.BindingFlags]'Static,NonPublic'
$extract = $type.GetMethod('ExtractWebSocketDebuggerUrl', $flags)
$response = $type.GetMethod('ResponseIsTrue', $flags)
$ready = $type.GetMethod('BuildReadyScript', $flags)
$inject = $type.GetMethod('BuildInjectionScript', $flags)
$verify = $type.GetMethod('BuildVerificationScript', $flags)

function Assert-Equal($Expected, $Actual, [string]$Message) {
    if ($Expected -cne $Actual) { throw "$Message (expected=$Expected, actual=$Actual)" }
}

$site = 'https://gyeon-ai.github.io/MyGomiPinball-Web/'
$tabs = '[{"url":"chrome://newtab/","webSocketDebuggerUrl":"ws://wrong"},{"url":"https://gyeon-ai.github.io/MyGomiPinball-Web/?names=test","webSocketDebuggerUrl":"ws://right"}]'
Assert-Equal 'ws://right' ($extract.Invoke($null, @($tabs, $site))) 'Exact site tab'
$lookalike = '[{"url":"https://gyeon-ai.github.io/MyGomiPinball-Web.invalid/","webSocketDebuggerUrl":"ws://wrong"}]'
Assert-Equal '' ($extract.Invoke($null, @($lookalike, $site))) 'Lookalike site rejection'

Assert-Equal $true ($response.Invoke($null, @('{"id":1,"result":{"result":{"type":"boolean","value":true}}}'))) 'CDP true response'
Assert-Equal $false ($response.Invoke($null, @('{"id":1,"result":{"result":{"type":"boolean","value":false}}}'))) 'CDP false response'
Assert-Equal $false ($response.Invoke($null, @('{"id":1,"error":{"data":{"value":true}}}'))) 'CDP error response'

Assert-Equal $true ([string]$ready.Invoke($null, @()) -like '*window.roulette.isReady*') 'Web readiness gate'
$names = '게임/2편*3,이름/3*2'
$injectionScript = [string]$inject.Invoke($null, @($names))
$verificationScript = [string]$verify.Invoke($null, @($names, [long]5))
Assert-Equal $true ($injectionScript.Contains("querySelector('#in_names')") -and !$injectionScript.Contains('querySelectorAll')) 'Exact name input'
Assert-Equal $true ($verificationScript.Contains('getCount()===5') -and $verificationScript.Contains($names)) 'Names and ball count verification'

Write-Output "PASS: $($assembly.GetName().Name) tab, response, readiness, input, verification"
