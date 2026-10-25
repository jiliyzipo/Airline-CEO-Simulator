#Requires -Version 5.1
<#
.SYNOPSIS
  跑最小航司模拟并与 baseline 对比（Phase 0.3 回归 / K13 年度基线刷新）。

.DESCRIPTION
  先验来源指纹，再比数值。

  为什么不能只比数字：`baseline/` 里的摘要只有在**输入模型没变**的前提下才叫基线。
  模型改过之后，同一份摘要的偏差是「换了一版模型」的结果，不是「引入了一个 bug」。
  之前只有整串相等判断，于是要么报一句无法解释的「不一致」，要么在 ±25% 的偏差上
  照旧打印对比结果 —— 两种输出都会把人带到错误结论上。

  口径（K13.2）：能不能比由 `baseline/BASELINE_SOURCE.json` 里的输入哈希决定。
  哈希不同 → 直接报错退出（exit 3），不打印百分比。
  哈希相同 → 按 ±Tolerance 逐字段比（默认 3%）。

.EXAMPLE
  .\compare_baseline.ps1
  .\compare_baseline.ps1 -UpdateBaseline
  .\compare_baseline.ps1 -Tolerance 0.03
#>
param(
    [int]$Seed = 42,
    [int]$Year = 1,
    [double]$Tolerance = 0.03,
    [string]$Delivery = "",
    [string]$Note = "",
    [switch]$UpdateBaseline
)

$ErrorActionPreference = "Stop"
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$root = $PSScriptRoot
Push-Location $root

try {
    $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
                [System.Environment]::GetEnvironmentVariable("Path", "User")

    $stampName = "BASELINE_SOURCE.json"
    $stampPath = Join-Path $root "baseline\$stampName"

    if ($UpdateBaseline) {
        $writeArgs = @("run", "--configuration", "Release", "--", "--baseline-write",
                       "--seed", $Seed, "--year", $Year)
        if ($Delivery) { $writeArgs += @("--delivery", $Delivery) }
        # `--note` 是 Program.cs 支持的字段，但旧版脚本不转发：传 -Note 进来会被静默丢掉，
        # 于是 BASELINE_SOURCE.json 的 Note 恒为空，没人看得出这次重录是为什么。
        if ($Note) { $writeArgs += @("--note", $Note) }
        & dotnet @writeArgs | Out-Host
        if ($LASTEXITCODE -ne 0) {
            Write-Host "FAIL: 重录基线失败（exit $LASTEXITCODE）" -ForegroundColor Red
            exit $LASTEXITCODE
        }
        if (-not (Test-Path $stampPath)) {
            Write-Host "FAIL: 重录后仍缺 $stampName —— 没有出处的基线不予接受" -ForegroundColor Red
            exit 2
        }
        Write-Host "OK: 基线已刷新，并写入来源指纹 $stampName" -ForegroundColor Green
        exit 0
    }

    # 指纹存在性先自查一遍，给出比工具内部更直白的提示
    if (-not (Test-Path $stampPath)) {
        Write-Host "FAIL: 缺来源指纹 tools/balance/baseline/$stampName" -ForegroundColor Red
        Write-Host "      旧基线没有出处，无法回答『这条基线是哪一版生成的』。" -ForegroundColor Yellow
        Write-Host "      不能拿来比。请先重录：.\compare_baseline.ps1 -UpdateBaseline" -ForegroundColor Yellow
        exit 2
    }

    $stamp = Get-Content $stampPath -Raw | ConvertFrom-Json
    if ([int]$stamp.seed -ne $Seed -or [int]$stamp.year -ne $Year) {
        Write-Host "FAIL: 基线指纹是 seed=$($stamp.seed) year=$($stamp.year)，本次请求 seed=$Seed year=$Year" -ForegroundColor Red
        Write-Host "      不是同一条基线，拒绝比较（不同 seed 的偏差没有解释力）。" -ForegroundColor Yellow
        exit 2
    }

    $commit = if ($stamp.commit.Length -ge 8) { $stamp.commit.Substring(0, 8) } else { $stamp.commit }
    $hash = if ($stamp.sourceHash.Length -ge 12) { $stamp.sourceHash.Substring(0, 12) } else { $stamp.sourceHash }
    Write-Host "基线来源: $commit ($($stamp.branch)) · $($stamp.generatedAt) · 输入 $($stamp.sourceFileCount) 文件 · hash $hash"
    if ($stamp.dirty) {
        Write-Host "注意: 该基线录制时工作区是脏的（未提交改动已计入输入哈希）。" -ForegroundColor Yellow
    }

    & dotnet run --configuration Release -- --baseline-compare `
        --seed $Seed --year $Year --tolerance $Tolerance | Out-Host
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        Write-Host "FAIL: 基线对比未通过（exit $code）" -ForegroundColor Red
    }
    exit $code
}
finally {
    Pop-Location
}
