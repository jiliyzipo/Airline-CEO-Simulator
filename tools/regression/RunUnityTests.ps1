param(
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [ValidateSet('EditMode', 'PlayMode')][string]$Platform = 'EditMode',
    [string]$Filter = '',
    [ValidatePattern('^[A-Za-z0-9_-]*$')][string]$RunName = '',
    [string]$UnityPath = 'C:/Program Files/Unity 6000.3.21f1/Editor/Unity.exe'
)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath $ProjectPath).Path
if (!(Get-Content -Raw -LiteralPath (Join-Path $project 'ProjectSettings/ProjectSettings.asset')).Contains('companyName: AirlineCEORegression')) {
    throw 'Tests must run in the isolated baseline export.'
}
if (@(Get-Process Unity -ErrorAction SilentlyContinue).Count -gt 0) { throw 'Unity occupied; no process will be closed.' }
$name = if ($RunName) { $RunName } else { $Platform }
$log = Join-Path $project ('Logs/Tasks20261017_' + $name + '.log')
$results = Join-Path $project ('Logs/Tasks20261017_' + $name + '.xml')
$unityArgs = @('-batchmode', '-nographics', '-projectPath', ('"' + $project + '"'),
    '-runTests', '-testPlatform', $Platform, '-testResults', ('"' + $results + '"'), '-logFile', ('"' + $log + '"'))
if ($Filter) { $unityArgs += @('-testFilter', $Filter) }
$process = Start-Process -FilePath $UnityPath -WindowStyle Hidden -ArgumentList $unityArgs -PassThru
$process.WaitForExit()
Write-Output ('UNITY_EXIT=' + $process.ExitCode)
if (!(Test-Path -LiteralPath $results)) { throw ('Missing NUnit result: ' + $results) }
[xml]$xml = Get-Content -Raw -LiteralPath $results
$run = $xml.'test-run'
Write-Output ('NUNIT result=' + $run.result + ' passed=' + $run.passed + ' failed=' + $run.failed + ' skipped=' + $run.skipped)
$xml.SelectNodes('//test-case[@result="Failed"]') | ForEach-Object { Write-Output ($_.fullname + ': ' + $_.failure.message.InnerText) }
if ($process.ExitCode -ne 0 -or $run.result -ne 'Passed' -or [int]$run.passed -eq 0) { exit 1 }
exit 0
