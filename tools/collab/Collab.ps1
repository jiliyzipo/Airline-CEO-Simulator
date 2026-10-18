# PowerShell wrapper for collaboration CLI (no complex JSON required for daily ops).
param(
    [Parameter(Position = 0)][string]$Command = 'help',
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $Root
# Force UTF-8 so Chinese task titles survive PowerShell 5.1 decoding.
try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    $OutputEncoding = [System.Text.Encoding]::UTF8
} catch { }

function Get-CollabRevision {
    $statePath = Join-Path $Root 'docs\collaboration\state.json'
    if (-not (Test-Path $statePath)) { throw 'state.json missing; run: .\tools\collab\Collab.ps1 init' }
    return (Get-Content $statePath -Raw -Encoding UTF8 | ConvertFrom-Json).revision
}

function Invoke-Collab {
    param([string[]]$CliArgs)
    & node 'tools\collab\cli.mjs' @CliArgs
    exit $LASTEXITCODE
}

switch ($Command.ToLowerInvariant()) {
    'help' {
        Write-Host @'
Collab.ps1 — Airline-CEO-Simulator collaboration helper

Usage:
  .\tools\collab\Collab.ps1 doctor
  .\tools\collab\Collab.ps1 init
  .\tools\collab\Collab.ps1 status
  .\tools\collab\Collab.ps1 sync
  .\tools\collab\Collab.ps1 ack cursor|codex
  .\tools\collab\Collab.ps1 claim C-DEMO
  .\tools\collab\Collab.ps1 deliver <task> path\to\summary.txt [model] [profiles]
  .\tools\collab\Collab.ps1 review-start <deliveryId> [profile] [actor] [authorization.json]
  .\tools\collab\Collab.ps1 review-run <reviewId> [actor] [authorization.json]
  .\tools\collab\Collab.ps1 review-finish <reviewId> path\to\evidence.json [actor] [authorization.json]
  .\tools\collab\Collab.ps1 review-evidence <reviewId> passed|failed AC1=passed,AC2=passed [note] [actor] [authorization.json]
  .\tools\collab\Collab.ps1 review-cancel <reviewId> [actor] [authorization.json]
  .\tools\collab\Collab.ps1 review-recover
  .\tools\collab\Collab.ps1 release C-DEMO
  .\tools\collab\Collab.ps1 watch
  .\tools\collab\Collab.ps1 tasks
  .\tools\collab\Collab.ps1 gate [taskId]
  .\tools\collab\Collab.ps1 task-add path\to\definition.json
  .\tools\collab\Collab.ps1 taskbook-baseline [taskId]
  .\tools\collab\Collab.ps1 taskbook-check <taskId>
  .\tools\collab\Collab.ps1 issue <issueId> <status> [note] [actor]
  .\tools\collab\Collab.ps1 hook cursor|codex <event> [payload.json]
  .\tools\collab\Collab.ps1 hooks-status

Stage automation (one command per side):
  .\tools\collab\Collab.ps1 stage open
  .\tools\collab\Collab.ps1 stage close [--task S01-UI] [--note "说明"] [--no-preflight] [--dry-run]
  .\tools\collab\Collab.ps1 stage review [--delivery DEL-...] [--profile smoke] [--coverage AC1=passed]
  .\tools\collab\Collab.ps1 stage fix
  .\tools\collab\Collab.ps1 stage board
'@
    }
    'doctor' { Invoke-Collab @('doctor', '--json') }
    'init' { Invoke-Collab @('init', '--json') }
    'status' { Invoke-Collab @('status', '--json') }
    'sync' { Invoke-Collab @('sync', '--json') }
    'ack' {
        $actor = $Rest[0]
        if (-not $actor) { throw 'ack requires actor cursor|codex' }
        $rev = Get-CollabRevision
        Invoke-Collab @('ack', '--actor', $actor, '--revision', "$rev", '--json')
    }
    'claim' {
        $task = $Rest[0]
        if (-not $task) { throw 'claim requires task id' }
        $rev = Get-CollabRevision
        Invoke-Collab @('claim', '--task', $task, '--actor', 'cursor', '--expected-revision', "$rev", '--json')
    }
    'deliver' {
        $task = $Rest[0]; $summary = $Rest[1]; $model = $Rest[2]
        $profiles = if ($Rest[3]) { $Rest[3] } else { 'smoke' }
        if (-not $task -or -not $summary) { throw 'deliver requires task and summary file' }
        $cliArgs = @('deliver', '--task', $task, '--actor', 'cursor', '--summary-file', $summary, '--profiles', $profiles, '--json')
        if ($model) { $cliArgs += @('--model', $model) }
        Invoke-Collab $cliArgs
    }
    'review-start' {
        $delivery = $Rest[0]; $profile = if ($Rest[1]) { $Rest[1] } else { 'smoke' }
        if (-not $delivery) { throw 'review-start requires deliveryId' }
        $actor = if ($Rest[2]) { $Rest[2] } else { 'codex' }
        $cliArgs = @('review-start', '--delivery', $delivery, '--actor', $actor, '--profile', $profile, '--json')
        if ($Rest[3]) { $cliArgs += @('--authorization', $Rest[3]) }
        Invoke-Collab $cliArgs
    }
    'review-run' {
        $review = $Rest[0]
        if (-not $review) { throw 'review-run requires reviewId' }
        $actor = if ($Rest[1]) { $Rest[1] } else { 'codex' }
        $cliArgs = @('review-run', '--review', $review, '--actor', $actor, '--json')
        if ($Rest[2]) { $cliArgs += @('--authorization', $Rest[2]) }
        Invoke-Collab $cliArgs
    }
    'review-finish' {
        $review = $Rest[0]; $ev = $Rest[1]
        if (-not $review -or -not $ev) { throw 'review-finish requires reviewId and evidence json' }
        $actor = if ($Rest[2]) { $Rest[2] } else { 'codex' }
        $cliArgs = @('review-finish', '--review', $review, '--actor', $actor, '--evidence', $ev, '--json')
        if ($Rest[3]) { $cliArgs += @('--authorization', $Rest[3]) }
        Invoke-Collab $cliArgs
    }
    'review-evidence' {
        if (-not $Rest[0] -or -not $Rest[1] -or -not $Rest[2]) { throw 'review-evidence requires reviewId conclusion explicit-coverage [note]' }
        $actor = if ($Rest[4]) { $Rest[4] } else { 'codex' }
        $cliArgs = @('review-evidence', '--review', $Rest[0], '--conclusion', $Rest[1], '--coverage', $Rest[2], '--actor', $actor, '--json')
        if ($Rest[3]) { $cliArgs += @('--note', $Rest[3]) }
        if ($Rest[5]) { $cliArgs += @('--authorization', $Rest[5]) }
        Invoke-Collab $cliArgs
    }
    'review-cancel' {
        $review = $Rest[0]
        if (-not $review) { throw 'review-cancel requires reviewId' }
        $actor = if ($Rest[1]) { $Rest[1] } else { 'codex' }
        $cliArgs = @('review-cancel', '--review', $review, '--actor', $actor, '--json')
        if ($Rest[2]) { $cliArgs += @('--authorization', $Rest[2]) }
        Invoke-Collab $cliArgs
    }
    'review-recover' { Invoke-Collab @('review-recover', '--actor', 'codex', '--json') }
    'release' {
        $task = $Rest[0]
        if (-not $task) { throw 'release requires task id' }
        Invoke-Collab @('release', '--task', $task, '--actor', 'cursor', '--json')
    }
    'watch' { Invoke-Collab @('watch') }
    'tasks' { Invoke-Collab @('tasks', '--json') }
    'gate' {
        $cliArgs = @('gate', '--json')
        if ($Rest[0]) { $cliArgs += @('--task', $Rest[0]) }
        Invoke-Collab $cliArgs
    }
    'task-add' {
        $def = $Rest[0]
        if (-not $def) { throw 'task-add requires a definition json path' }
        Invoke-Collab @('task-add', '--definition', $def, '--actor', 'cursor', '--json')
    }
    'taskbook-baseline' {
        $cliArgs = @('taskbook-baseline', '--json')
        if ($Rest[0]) { $cliArgs += @('--task', $Rest[0]) }
        Invoke-Collab $cliArgs
    }
    'taskbook-check' {
        $task = $Rest[0]
        if (-not $task) { throw 'taskbook-check requires task id' }
        Invoke-Collab @('taskbook-check', '--task', $task, '--actor', 'codex', '--json')
    }
    'issue' {
        $issueId = $Rest[0]; $status = $Rest[1]; $note = $Rest[2]
        if (-not $issueId -or -not $status) { throw 'issue requires issueId and status' }
        $actor = if ($Rest[3]) { $Rest[3] } else { 'codex' }
        $cliArgs = @('issue-update', '--issue', $issueId, '--status', $status, '--actor', $actor, '--json')
        if ($note) { $cliArgs += @('--note', $note) }
        Invoke-Collab $cliArgs
    }
    'hook' {
        $source = $Rest[0]; $event = $Rest[1]; $payload = $Rest[2]
        if (-not $source -or -not $event) { throw 'hook requires source cursor|codex and event name' }
        $cliArgs = @('hook', '--source', $source, '--event', $event, '--json')
        if ($payload) { $cliArgs += @('--payload', $payload) }
        Invoke-Collab $cliArgs
    }
    'hooks-status' { Invoke-Collab @('hooks-status', '--json') }
    'session' { Invoke-Collab @('stage-open') }
    'stage' {
        $sub = if ($Rest.Count -ge 1 -and $Rest[0]) { $Rest[0] } else { 'board' }
        $names = @{
            open = 'stage-open'; close = 'stage-close'; review = 'stage-review'
            fix = 'stage-fix'; board = 'stage-board'; report = 'stage-report'
        }
        $cliName = $names[$sub]
        if (-not $cliName) { throw "Unknown stage subcommand: $sub (open|close|review|fix|board)" }
        $extra = @()
        if ($Rest.Count -gt 1) { $extra = $Rest[1..($Rest.Count - 1)] }
        Invoke-Collab (@($cliName) + $extra)
    }
    default { throw "Unknown command: $Command" }
}
