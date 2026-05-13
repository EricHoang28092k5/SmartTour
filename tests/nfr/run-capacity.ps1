param(
    [string]$BaseUrl = "https://localhost:7xxx",
    [string]$PoiId = "1",
    [string]$OutputDir = "tests/nfr/results"
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent (Split-Path -Parent $scriptRoot)
$arrivalScript = Join-Path $scriptRoot "smarttour-visit-arrival.js"

$levels = @(
    @{ Name = "low";  Arrival = 25 },
    @{ Name = "med";  Arrival = 45 },
    @{ Name = "high"; Arrival = 58 }
)

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"

if (!(Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

Write-Host "SmartTour NFR capacity (visit arrival rate)"
Write-Host "BaseUrl : $BaseUrl"
Write-Host "POI_ID  : $PoiId"
Write-Host "Script  : $arrivalScript"
Write-Host "Output  : $OutputDir"
Write-Host ""

foreach ($lvl in $levels) {
    $outFile = Join-Path $OutputDir "capacity-visit-$($lvl.Name)-$timestamp.log"
    Write-Host "=== ARRIVAL_PER_10S=$($lvl.Arrival) ($($lvl.Name)) ==="

    Push-Location $repoRoot
    try {
        $argList = @(
            "run",
            "-e", "BASE_URL=$BaseUrl",
            "-e", "POI_ID=$PoiId",
            "-e", "ARRIVAL_PER_10S=$($lvl.Arrival)",
            "-e", "DURATION=90s",
            $arrivalScript
        )
        & k6 @argList *>&1 | Tee-Object -FilePath $outFile
    }
    finally {
        Pop-Location
    }

    Write-Host "=== Done $($lvl.Name) ==="
    Write-Host ""
}

Write-Host "Hoàn tất. Xem log trong: $OutputDir"
