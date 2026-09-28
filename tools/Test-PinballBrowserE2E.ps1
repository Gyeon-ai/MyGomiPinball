param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [string]$Names = 'alpha,beta*2',
    [long]$ExpectedCoins = -1,
    [switch]$ThroughButton
)

$ErrorActionPreference = 'Stop'
$url = 'https://gyeon-ai.github.io/MyGomiPinball-Web/'
$flags = [Reflection.BindingFlags]'Static,NonPublic,Public'
$existingProfiles = @{}
Get-ChildItem -LiteralPath $env:TEMP -Directory -Filter 'MyGomiPinballBrowser-*' -ErrorAction SilentlyContinue |
    ForEach-Object { $existingProfiles[$_.FullName] = $true }

$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $ExePath).Path)
$injector = $assembly.GetType('MyGomiPinballAuto.PinballSiteInjector', $true)
$open = $injector.GetMethod('OpenAndInjectAsync', $flags)
$extract = $injector.GetMethod('ExtractWebSocketDebuggerUrl', $flags)
$evaluate = $injector.GetMethod('EvaluateBooleanAsync', $flags)
$profile = $null
$form = $null

try {
    $injectionError = $null
    if ($ThroughButton) {
        Add-Type -AssemblyName System.Drawing
        Add-Type -AssemblyName System.Windows.Forms
        Add-Type -AssemblyName System.Web
        Add-Type -AssemblyName UIAutomationClient
        $formType = $assembly.GetType('MyGomiPinballAuto.MainForm', $true)
        $form = [Activator]::CreateInstance($formType, [Reflection.BindingFlags]'Instance,Public,NonPublic', $null, @(), $null)
        $form.ShowInTaskbar = $false
        $form.StartPosition = [Windows.Forms.FormStartPosition]::Manual
        $form.Location = New-Object Drawing.Point(-20000, -20000)
        $form.Show()
        $formType.GetField('_pinballText', [Reflection.BindingFlags]'Instance,NonPublic').GetValue($form).Text = $Names
        $button = $formType.GetField('_openPinballButton', [Reflection.BindingFlags]'Instance,NonPublic').GetValue($form)
        $click = [Windows.Forms.Control].GetMethod('OnClick', [Reflection.BindingFlags]'Instance,NonPublic')
        $click.Invoke($button, @([EventArgs]::Empty)) | Out-Null
        $opened = $false
        $buttonDeadline = [DateTime]::UtcNow.AddSeconds(15)
        $browserTitle = [string]::Concat([char]0xACF0, [char]0xC774, ' ', [char]0xD540, [char]0xBCFC, ' - Chrome')
        $editCondition = New-Object Windows.Automation.PropertyCondition(
            [Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Edit)
        do {
            [Windows.Forms.Application]::DoEvents()
            foreach ($browser in @(Get-Process chrome -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -eq $browserTitle -and $_.MainWindowHandle -ne 0 })) {
                $window = [Windows.Automation.AutomationElement]::FromHandle($browser.MainWindowHandle)
                $edits = $window.FindAll([Windows.Automation.TreeScope]::Descendants, $editCondition)
                for ($i = 0; $i -lt $edits.Count; $i++) {
                    $pattern = $null
                    if (!$edits.Item($i).TryGetCurrentPattern([Windows.Automation.ValuePattern]::Pattern, [ref]$pattern)) { continue }
                    $address = ([Windows.Automation.ValuePattern]$pattern).Current.Value
                    if (!$address.Contains('MyGomiPinball-Web/?names=')) { continue }
                    if (!$address.StartsWith('http')) { $address = 'https://' + $address }
                    $uri = [Uri]$address
                    if ($uri.Host -eq 'gyeon-ai.github.io' -and $uri.AbsolutePath.TrimEnd('/') -eq '/MyGomiPinball-Web' -and
                        [System.Web.HttpUtility]::ParseQueryString($uri.Query)['names'] -eq $Names) {
                        $opened = $true
                        break
                    }
                }
                if ($opened) { break }
            }
            if (!$opened) { Start-Sleep -Milliseconds 200 }
        } while (!$opened -and [DateTime]::UtcNow -lt $buttonDeadline)
        if (!$opened) { throw 'The button did not open Chrome with the expected names URL.' }
        Write-Output "PASS: $ExePath button opened Chrome with the expected names URL without using the clipboard."
        return
    }
    else {
        if ($ExpectedCoins -lt 0) { throw 'ExpectedCoins is required for injector verification.' }
        try { $injected = $open.Invoke($null, @($url, $Names, [long]$ExpectedCoins)).GetAwaiter().GetResult() }
        catch { $injectionError = $_ }
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(35)
    do {
        if ($form) { [Windows.Forms.Application]::DoEvents() }
        $profiles = @(Get-ChildItem -LiteralPath $env:TEMP -Directory -Filter 'MyGomiPinballBrowser-*' -ErrorAction SilentlyContinue |
            Where-Object { !$existingProfiles.ContainsKey($_.FullName) })
        if ($profiles.Count -gt 0 -or !$ThroughButton) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($profiles.Count -ne 1) {
        throw "Expected one new browser profile, found $($profiles.Count)."
    }

    $profile = $profiles[0].FullName
    $port = [int]($profiles[0].Name -replace '^MyGomiPinballBrowser-', '')
    if ($injectionError) {
        throw $injectionError
    }
    if (!$ThroughButton -and !$injected.Injected) {
        throw 'The injector did not verify the names and ball count.'
    }
    $client = New-Object Net.WebClient
    $client.Encoding = [Text.Encoding]::UTF8
    $serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
    $expected = $serializer.Serialize($Names)
    $check = "document.readyState==='complete' && document.querySelector('#in_names') && document.querySelector('#in_names').value===$expected"
    if ($ExpectedCoins -ge 0) {
        $check += " && window.roulette && window.roulette.getCount()===$ExpectedCoins"
    }
    $matched = $false
    do {
        if ($form) { [Windows.Forms.Application]::DoEvents() }
        try {
            $tabs = $client.DownloadString("http://127.0.0.1:$port/json/list")
            $webSocketUrl = $extract.Invoke($null, @($tabs, $url))
            if (![string]::IsNullOrEmpty($webSocketUrl)) {
                $matched = $evaluate.Invoke($null, @($webSocketUrl, $check)).GetAwaiter().GetResult()
            }
        }
        catch { }
        if ($matched -or !$ThroughButton) { break }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)
    if (!$matched) { throw 'The published page did not receive the names from the collector.' }

    Start-Sleep -Seconds 2
    $retained = $evaluate.Invoke($null, @($webSocketUrl, $check)).GetAwaiter().GetResult()
    if (!$retained) {
        throw 'The injected names were lost after the page finished loading.'
    }

    $route = if ($ThroughButton) { 'button click' } else { 'injector' }
    Write-Output "PASS: $ExePath $route opened Chrome, injected names, and retained them after page load."
}
finally {
    if ($form) { $form.Dispose() }
    if ($profile) {
        Get-CimInstance Win32_Process -Filter "Name = 'chrome.exe'" -ErrorAction SilentlyContinue |
            Where-Object { $_.CommandLine -and $_.CommandLine.Contains($profile) } |
            ForEach-Object { Stop-Process -Id $_.ProcessId -ErrorAction SilentlyContinue }
    }
}
