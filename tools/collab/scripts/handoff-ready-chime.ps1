# Loud handoff chime after stage-close succeeds.
# Detached by tools/collab/lib/handoff-notify.mjs — never blocks the CLI.
param(
    [string]$Message = '交付已就绪，请交给 Codex 验收'
)

$ErrorActionPreference = 'SilentlyContinue'

$wavCandidates = @(
    (Join-Path $env:WINDIR 'Media\Alarm05.wav'),
    (Join-Path $env:WINDIR 'Media\Alarm01.wav'),
    (Join-Path $env:WINDIR 'Media\Windows Foreground.wav'),
    (Join-Path $env:WINDIR 'Media\Windows Notify System Generic.wav')
)
$wav = $wavCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

try {
    Add-Type -AssemblyName System.Speech | Out-Null
    $speak = New-Object System.Speech.Synthesis.SpeechSynthesizer
    $speak.Volume = 100
    $speak.Rate = -1
    $speak.Speak($Message)
    $speak.Dispose()
} catch {}

if ($wav) {
    try {
        Add-Type -AssemblyName System.Windows.Forms | Out-Null
        $player = New-Object System.Media.SoundPlayer $wav
        $player.PlaySync()
        Start-Sleep -Milliseconds 200
        $player.PlaySync()
        $player.Dispose()
    } catch {
        [Console]::Beep(988, 350)
        [Console]::Beep(1319, 550)
    }
} else {
    [Console]::Beep(988, 350)
    [Console]::Beep(1319, 550)
}

exit 0
