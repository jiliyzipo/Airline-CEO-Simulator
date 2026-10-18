param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^AirlineCEO\.[A-Za-z0-9_.]+$')]
    [string]$Method,
    [ValidatePattern('^[A-Za-z0-9_-]+$')]
    [string]$LogName = 'DevelopmentRegression',
    [string]$ProjectPath = (Join-Path $PSScriptRoot '../../Unity Project'),
    [string]$UnityPath = 'C:/Program Files/Unity 6000.3.21f1/Editor/Unity.exe',
    [switch]$AsyncMethod
)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath $ProjectPath).Path
$running = @(Get-Process Unity -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    $running | Select-Object Id, MainWindowTitle | Format-Table
    throw 'Unity is occupied. Save and close the editor before running; this script never closes it.'
}
$logDir = Join-Path $project 'Logs'
[IO.Directory]::CreateDirectory($logDir) | Out-Null
$logPath = Join-Path $logDir ($LogName + '.log')
$unityArgs = @('-batchmode', '-nographics', '-projectPath', ('"' + $project + '"'),
    '-executeMethod', $Method, '-logFile', ('"' + $logPath + '"'))
if (!$AsyncMethod) { $unityArgs += '-quit' }
$process = Start-Process -FilePath $UnityPath -WindowStyle Hidden -ArgumentList $unityArgs -PassThru
# Wait only for the editor process, not its persistent licensing-client descendants.
$process.WaitForExit()
Write-Output ('UNITY_EXIT=' + $process.ExitCode)
Write-Output ('UNITY_LOG=' + $logPath)
if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath -Tail 12 }
exit $process.ExitCode
