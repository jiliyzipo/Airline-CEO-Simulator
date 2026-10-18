#Requires -Version 5.1
<#
.SYNOPSIS
  跑最小航司模拟并与 baseline 对比（Phase 0.3 回归）。
.EXAMPLE
  .\compare_baseline.ps1
  .\compare_baseline.ps1 -Seed 42 -UpdateBaseline
#>
param(
    [int]$Seed = 42,
    [int]$Year = 1,
    [switch]$UpdateBaseline
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
Set-Location $root

$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" +
            [System.Environment]::GetEnvironmentVariable("Path","User")

dotnet run --configuration Release -- --seed $Seed --year $Year --out out | Out-Host
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$summaryName = "summary_y${Year}_seed${Seed}.txt"
$current = Join-Path $root "out\$summaryName"
$baseline = Join-Path $root "baseline\$summaryName"

if ($UpdateBaseline) {
    New-Item -ItemType Directory -Force -Path (Join-Path $root "baseline") | Out-Null
    Copy-Item $current $baseline -Force
    $pnl = "pnl_y${Year}_seed${Seed}.csv"
    Copy-Item (Join-Path $root "out\$pnl") (Join-Path $root "baseline\$pnl") -Force
    Write-Host "Baseline updated: $baseline"
    exit 0
}

if (-not (Test-Path $baseline)) {
    Write-Error "Missing baseline: $baseline  (run with -UpdateBaseline first)"
}

$a = Get-Content $current -Raw
$b = Get-Content $baseline -Raw
if ($a -ne $b) {
    Write-Host "REGRESSION DIFF detected:" -ForegroundColor Red
    Write-Host "---- baseline ----"
    Write-Host $b
    Write-Host "---- current ----"
    Write-Host $a
    exit 1
}

Write-Host "OK: summary matches baseline ($summaryName)" -ForegroundColor Green
exit 0
