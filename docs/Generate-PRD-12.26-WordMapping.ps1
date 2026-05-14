#Requires -Version 5.1
# PRD 12.26 — Vendor mua Premium bang vi (CMS -> API): anh xa sequence -> ma nguon
$ErrorActionPreference = 'Stop'
$base = $PSScriptRoot
$tsvPath = Join-Path $base 'PRD_12.26_mapping_sections.tsv'
$outPath = Join-Path $base 'PRD_12.26_VendorPremiumWallet_CodeMapping.docx'

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

Add-H1 'PRD 12.26 - Vendor mua Premium bang vi (CMS -> Backend API)'
Add-P 'Nguon: PRD_SmartTour.md muc 12.26. Bang: PRD_12.26_mapping_sections.tsv (UTF-8).'
Add-P 'Endpoint API: POST /api/vendor/premium/purchase-premium-wallet-cms + header X-Internal-Key (BackendApi:InternalKey tren CMS = Admin:ApiKey tren API).'
Add-P ''

function Get-SectionRows([string]$sec) {
    $list = New-Object 'System.Collections.Generic.List[object]'
    foreach ($r in $rows) {
        if ($r.section -eq $sec) { $list.Add($r) }
    }
    return $list
}

Add-H1 '1. Giao dien CMS (Vendor)'
Add-Table (Get-SectionRows 'ui')

Add-H1 '2. PremiumController (CMS)'
Add-Table (Get-SectionRows 'cms_get')
Add-Table (Get-SectionRows 'cms_post')

Add-H1 '3. VendorPremiumController (API)'
Add-Table (Get-SectionRows 'api')

Add-H1 '4. PremiumWalletPurchaseService + VendorWalletService'
Add-Table (Get-SectionRows 'pw')
Add-Table (Get-SectionRows 'wallet')

Add-H1 '5. DTO / cau hinh'
Add-Table (Get-SectionRows 'dto')
Add-P 'Cau hinh CMS: appsettings — section BackendApi:BaseUrl, InternalKey.'
Add-P 'Cau hinh API: Admin:ApiKey (doi chieu X-Internal-Key).'
Add-P ''

$doc.SaveAs2($outPath)
$doc.Close($false)
$word.Quit()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($doc) | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null
[System.GC]::Collect()
Write-Host "Saved: $outPath"
