#Requires -Version 5.1
# Build Word doc from UTF-8 TSV (avoids encoding issues in .ps1 source)
$ErrorActionPreference = 'Stop'
$base = $PSScriptRoot
$tsvPath = Join-Path $base 'PRD_12.2_mapping_sections.tsv'
$outPath = Join-Path $base 'PRD_12.2_TaoPOI_AnhXaMaNguon.docx'

if (-not (Test-Path $tsvPath)) { throw "Missing TSV: $tsvPath" }

$rows = Import-Csv -LiteralPath $tsvPath -Delimiter "`t" -Encoding UTF8

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$doc = $word.Documents.Add()
$sel = $word.Selection

function Add-H1([string]$t) {
    $sel.Style = 'Heading 1'
    $sel.TypeText($t)
    $sel.TypeParagraph()
    $sel.Style = 'Normal'
}

function Add-P([string]$t) {
    $sel.TypeText($t)
    $sel.TypeParagraph()
}

function Add-Table([System.Collections.Generic.List[object]]$dataRows) {
    if ($dataRows.Count -eq 0) { return }
    $headers = @('STT','Buoc sequence','File','Ham / thanh vien','Dong code','Ghi chu')
    $rCount = 1 + $dataRows.Count
    $cCount = $headers.Count
    $sel.EndKey(6) | Out-Null
    $tbl = $doc.Tables.Add($sel.Range, $rCount, $cCount)
    for ($c = 0; $c -lt $cCount; $c++) { $tbl.Cell(1, $c + 1).Range.Text = $headers[$c] }
    for ($r = 0; $r -lt $dataRows.Count; $r++) {
        $o = $dataRows[$r]
        $tbl.Cell($r + 2, 1).Range.Text = [string]$o.order
        $tbl.Cell($r + 2, 2).Range.Text = [string]$o.step
        $tbl.Cell($r + 2, 3).Range.Text = [string]$o.file
        $tbl.Cell($r + 2, 4).Range.Text = [string]$o.method
        $tbl.Cell($r + 2, 5).Range.Text = [string]$o.line_range
        $tbl.Cell($r + 2, 6).Range.Text = [string]$o.notes_vi
    }
    $tbl.Range.Font.Size = 10
    $tbl.Rows.Item(1).Range.Font.Bold = $true
    $tbl.Range.InsertParagraphAfter()
    $sel.EndKey(6) | Out-Null
}

Add-H1 'PRD 12.2 - Tao POI (Admin/Vendor): anh xa sequence -> ma nguon'
Add-P 'Nguon: PRD_SmartTour.md muc 12.2. Bang duoc sinh tu PRD_12.2_mapping_sections.tsv (UTF-8).'
Add-P ''

function Get-SectionRows([string]$sec) {
    $list = New-Object 'System.Collections.Generic.List[object]'
    foreach ($r in $rows) {
        if ($r.section -eq $sec) { $list.Add($r) }
    }
    return $list
}

Add-H1 '1. Luong Admin'
Add-Table (Get-SectionRows 'admin')

Add-H1 '2. Vendor - POST Create -> draft -> GET CreateConfirm'
Add-Table (Get-SectionRows 'vendor_a')

Add-H1 '3. Vendor - POST CreateConfirmPost (transaction: debit -> Poi -> translations + TTS + Cloudinary)'
Add-Table (Get-SectionRows 'vendor_b')

Add-H1 '4. DI (SmartTourCMS)'
Add-P 'Program.cs: AddScoped<IVoiceService, VoiceService> — tim chuoi IVoiceService trong SmartTourCMS/Program.cs.'
Add-P 'VendorWalletService: dang ky Scoped cung AppDbContext trong CMS Program.cs.'
Add-P ''

Add-H1 '5. Huy xac nhan'
Add-P 'CreateConfirmCancel: SmartTourCMS/Controllers/PoiController.cs, dong 370-377.'
Add-P ''

$doc.SaveAs2($outPath)
$doc.Close($false)
$word.Quit()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($doc) | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null
[System.GC]::Collect()
Write-Host "Saved: $outPath"
