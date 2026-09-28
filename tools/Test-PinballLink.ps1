param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [Parameter(Mandatory = $true)][string]$NamespaceName,
    [Parameter(Mandatory = $true)][string]$ExpectedVersion
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
$flags = [Reflection.BindingFlags]'Instance,Static,Public,NonPublic'
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $ExePath).Path)
$type = $assembly.GetType("$NamespaceName.MainForm", $true)
$form = [Activator]::CreateInstance($type, $flags, $null, @(), $null)
$siteUrl = 'https://gyeon-ai.github.io/MyGomiPinball-Web/'

try {
    if ($assembly.GetName().Version.ToString() -ne $ExpectedVersion) { throw 'Unexpected assembly version.' }

    $buildUrl = $type.GetMethod('BuildPinballSiteUrl', $flags, $null, [type[]]@([string]), $null)
    if ($null -eq $buildUrl) { throw 'URL builder was not found.' }
    if ($buildUrl.Invoke($form, @('')) -ne $siteUrl) { throw 'Empty list did not use the published site URL.' }

    $names = 'alpha,beta*2'
    $expected = $siteUrl + '?names=' + [Uri]::EscapeDataString($names)
    if ($buildUrl.Invoke($form, @($names)) -ne $expected) { throw 'List URL was not encoded correctly.' }

    $normalize = $type.GetMethod('NormalizePinballNames', $flags)
    if ($normalize.Invoke($form, @("  alpha , beta*2`n gamma  ")) -ne 'alpha,beta*2,gamma') {
        throw 'List normalization changed unexpectedly.'
    }

    $injector = $assembly.GetType("$NamespaceName.PinballSiteInjector", $false)
    if ($null -ne $injector) {
        $script = $injector.GetMethod('BuildInjectionScript', $flags).Invoke($null, @($names))
        if (!$script.Contains("document.querySelector('#in_names')")) { throw 'Auto input does not target the published name field.' }
        if (!$script.Contains("maps.options.length===0")) { throw 'Auto input can run before the site initializes.' }
        if ($script.Contains("querySelectorAll('textarea,input')")) { throw 'Auto input still scans unrelated fields.' }
    }

    Write-Output "PASS: $NamespaceName $ExpectedVersion published URL, names encoding, input target"
}
finally { $form.Dispose() }
