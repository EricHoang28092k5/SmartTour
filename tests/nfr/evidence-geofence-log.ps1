param(
    [string]$BaseUrl = "https://localhost:7xxx",
    [int]$PoiId = 1,
    [int]$VisitCount = 24,
    [string]$OutputDir = "tests/nfr/results"
)

$ErrorActionPreference = "Stop"
$BaseUrl = $BaseUrl.TrimEnd("/")

if (!(Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$outFile = Join-Path $OutputDir "smarttour-geofence-evidence-$stamp.txt"
$sb = [System.Text.StringBuilder]::new()
function W([string]$line) {
    [void]$sb.AppendLine($line)
    Write-Host $line
}

W "=== SmartTour — minh chứng API visit + gợi ý đối chiếu log (Geofence / k6 / CMS) ==="
W "BaseUrl    : $BaseUrl"
W "PoiId      : $PoiId"
W "VisitCount : $VisitCount"
W ""

try {
    $health = Invoke-RestMethod -Uri "$BaseUrl/api/health" -Method Get -Headers @{ "Accept" = "application/json" }
    W "GET /api/health OK:"
    W ($health | ConvertTo-Json -Depth 5 -Compress)
}
catch {
    W "GET /api/health FAILED: $($_.Exception.Message)"
}

W ""
W "--- POST /api/analytics/visit (MapClick=1), userId SIM-EVD-* ---"
W "Lưu ý: DeviceTokenPolicy ~60 request / 10s / IP — script gửi theo batch + nghỉ 10.5s."
W ""

$headers = @{
    "Content-Type" = "application/json"
    "Accept"       = "application/json"
}

$ok = 0
$fail = 0
$batch = 0
for ($i = 1; $i -le $VisitCount; $i++) {
    if ((($i - 1) % 55) -eq 0 -and $i -gt 1) {
        $batch++
        W "[pause] batch $batch — sleep 10.5s (rate limit)"
        Start-Sleep -Milliseconds 10500
    }

    $body = @{
        poiId     = $PoiId
        userId    = "SIM-EVD-{0:D4}" -f $i
        lat       = 10.7769
        lng       = 106.7008
        visitType = 1
    } | ConvertTo-Json -Compress

    try {
        $r = Invoke-WebRequest -Uri "$BaseUrl/api/analytics/visit" -Method Post -Headers $headers -Body $body -UseBasicParsing
        if ($r.StatusCode -eq 202) { $ok++ }
        else { $fail++; W "[$i] HTTP $($r.StatusCode)" }
    }
    catch {
        $fail++
        W "[$i] ERROR: $($_.Exception.Message)"
    }
}

W ""
W "Tóm tắt: accepted(202)=$ok failed=$fail"
W ""
W "Đối chiếu DB: đăng nhập CMS Admin → Geofence Simulator → panel **Server log** (SIM-*), hoặc API đã ghi visit_logs sau worker."
W "File log client (ILogRunner): CMS → Geofence Simulator → Save / Tải logqueue.txt (mặc định %TEMP%\SmartTour\logqueue.txt)."
W ""

[System.IO.File]::WriteAllText($outFile, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))
Write-Host "Đã ghi: $outFile"
