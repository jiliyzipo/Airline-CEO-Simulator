# Find which byte sequences decode to U+E7C8 (and relatives) under .NET CP936.
$enc = [System.Text.Encoding]::GetEncoding(936)
$target = [char]0xE7C8
$hits = @()
for ($lead = 0x81; $lead -le 0xFE; $lead++) {
    for ($trail = 0x00; $trail -le 0xFF; $trail++) {
        $s = $enc.GetString([byte[]]@([byte]$lead, [byte]$trail))
        if ($s.Length -eq 1 -and $s[0] -eq $target) { $hits += ('pair {0:X2} {1:X2}' -f $lead, $trail) }
    }
}
for ($b = 0x80; $b -le 0xFF; $b++) {
    $s = $enc.GetString([byte[]]@([byte]$b))
    if ($s.Length -eq 1 -and $s[0] -eq $target) { $hits += ('single {0:X2}' -f $b) }
}
if ($hits.Count -eq 0) { Write-Output 'no hits for U+E7C8' } else { $hits | ForEach-Object { Write-Output $_ } }
