param(
    [Parameter(Mandatory=$true)][string]$ExePath,
    [string]$NamespaceName = 'MyGomiPinball',
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()
[Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false)
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $ExePath).Path)
$type = $assembly.GetType("$NamespaceName.UpdatePromptDialog", $true)
$flags = [Reflection.BindingFlags]'Instance,Public,NonPublic'
$notes = @(
    '창 크기에 따른 버튼 잘림, 큰 코인 합계 표시, 목록 편집 입력칸 정렬을 개선했습니다. 기존 문구·글꼴·수집 및 코인 규칙은 유지합니다.',
    ('업데이트 내용을 확인합니다. ' * 20).Substring(0, 180),
    'Update test notes',
    "첫 번째 변경 내용을 확인합니다.`r`n두 번째 변경 내용도 그대로 표시됩니다.`r`n세 번째 항목의 여백을 확인합니다."
)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$results = @()
function Get-TestControls($parent) {
    foreach ($child in $parent.Controls) {
        $child
        Get-TestControls $child
    }
}
for ($index=0; $index -lt $notes.Count; $index++) {
    $dialog = [Activator]::CreateInstance($type, $flags, $null, @($assembly.GetName().Version, $notes[$index]), $null)
    try {
        $dialog.StartPosition = 'Manual'
        $dialog.Location = New-Object Drawing.Point(-20000, -20000)
        $dialog.Show()
        [Windows.Forms.Application]::DoEvents()
        if ($dialog.Controls['caption']) { throw 'Old caption row is still visible' }
        $close = $dialog.Controls['closeButton']
        $title = $dialog.Controls['title']
        $version = $dialog.Controls['version']
        $notesBox = $dialog.Controls['notesBox']
        if (!$notesBox) { throw 'Update notes box is missing' }
        $notesTitle = $notesBox.Controls['notesTitle']
        $body = $notesBox.Controls['notes']
        $later = $dialog.Controls['laterButton']
        $update = $dialog.Controls['updateButton']
        foreach ($control in @(Get-TestControls $dialog)) {
            if (!$control.Parent.ClientRectangle.Contains($control.Bounds)) { throw "Outside parent: $($control.Name)" }
            $size = [Windows.Forms.TextRenderer]::MeasureText($control.Text, $control.Font,
                (New-Object Drawing.Size($control.Width, [int]::MaxValue)),
                [Windows.Forms.TextFormatFlags]'NoPadding,NoPrefix,WordBreak')
            if ($size.Height -gt $control.Height) { throw "Clipped text: $($control.Name)" }
        }
        if ($title.Left -ne 18 -or $title.Left -ne $version.Left -or $version.Left -ne $notesBox.Left) { throw 'Left edges differ' }
        if ($title.Top -ne 20 -or $close.Top + $close.Height / 2 -ne $title.Top + $title.Height / 2) { throw 'Title and close button alignment differs' }
        if ($title.Right + 8 -gt $close.Left -or $close.Right -ne $notesBox.Right) { throw 'Close button spacing differs' }
        if ($dialog.ControlBox) { throw 'Native caption controls should not be visible' }
        if ($dialog.ClientSize.Width - $update.Right -ne $notesBox.Left -or $notesBox.Right -ne $update.Right) { throw 'Right margin differs' }
        if ($notesTitle.Text -cne '업데이트 내용' -or $notesBox.AccessibleName -cne '업데이트 내용') { throw 'Update notes heading changed' }
        if ($notesTitle.Left -ne $body.Left -or $notesTitle.Width -ne $body.Width -or
            $body.Left -ne 14 -or $notesBox.Width - $body.Right -ne 14 -or
            $notesBox.Height - $body.Bottom -ne 14) { throw 'Notes box padding differs' }
        if ($body.Top - $notesTitle.Bottom -lt 6) { throw 'Notes title overlaps body' }
        if ($notesBox.FillColor -ne [Drawing.Color]::White -or $notesBox.BorderColor -eq $notesBox.FillColor) { throw 'Notes box is not distinct' }
        if ($update.Size -ne $later.Size -or $update.Top -ne $later.Top) { throw 'Button alignment differs' }
        if ($later.Top - $notesBox.Bottom -ne 16 -or $dialog.ClientSize.Height - $update.Bottom -ne 16) { throw 'Button spacing differs' }
        if ($body.Text -cne $notes[$index]) { throw 'Notes were modified' }
        if ($title.Text -cne '새 버전을 사용할 수 있습니다') { throw 'Title changed' }
        if ($later.Text -cne '나중에' -or $update.Text -cne '업데이트') { throw 'Button wording changed' }
        if ($dialog.AcceptButton -ne $update -or $dialog.CancelButton -ne $later) { throw 'Keyboard bindings changed' }
        if ($title.Font.Size -ne 13 -or $later.Font.Size -ne 10 -or $body.Font.Size -ne 10) { throw 'Unexpected font size' }
        $bitmap = New-Object Drawing.Bitmap($dialog.ClientSize.Width, $dialog.ClientSize.Height)
        try {
            $dialog.DrawToBitmap($bitmap, $dialog.ClientRectangle)
            $bitmap.Save((Join-Path $OutputDirectory "prompt-$index.png"), [Drawing.Imaging.ImageFormat]::Png)
        } finally { $bitmap.Dispose() }
        $later.PerformClick()
        if ($dialog.DialogResult -ne [Windows.Forms.DialogResult]::Cancel) { throw 'Later button failed' }
        $update.PerformClick()
        if ($dialog.DialogResult -ne [Windows.Forms.DialogResult]::OK) { throw 'Update button failed' }
        $close.PerformClick()
        if ($dialog.DialogResult -ne [Windows.Forms.DialogResult]::Cancel) { throw 'Close button failed' }
        $results += [pscustomobject]@{NoteLength=$body.Text.Length;Client=$dialog.ClientSize.ToString();Left=$notesBox.Left;Right=$dialog.ClientSize.Width-$update.Right;ButtonGap=$update.Left-$later.Right;Box=$notesBox.Bounds.ToString();NotesHeight=$body.Height;Font=$body.Font.Name}
    } finally { $dialog.Dispose() }
}
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'dialog.json') -Encoding UTF8
Write-Output "PASS: $NamespaceName dialog, $($notes.Count) note cases, notes box, alignment, clipping, fonts and actions"
