# Dump the .NET CP936 decoder behaviour to JSON so the corruption can be modelled exactly.
$enc = [System.Text.Encoding]::GetEncoding(936)
$pairs = @{}
$singles = @{}
$bad = @()

for ($lead = 0x81; $lead -le 0xFE; $lead++) {
    $buf = New-Object System.Collections.Generic.List[byte]
    $trails = New-Object System.Collections.Generic.List[int]
    for ($trail = 0; $trail -le 0xFF; $trail++) {
        if ($trail -eq 0x41) { continue }
        $buf.Add([byte]$lead); $buf.Add([byte]$trail); $buf.Add([byte]0x41)
        $trails.Add($trail)
    }
    $s = $enc.GetString($buf.ToArray())
    $ok = $true
    for ($k = 0; $k -lt $trails.Count; $k++) {
        if (($k * 2 + 1) -ge $s.Length) { $ok = $false; break }
        if ($s[$k * 2 + 1] -ne [char]0x41) { $ok = $false; break }
    }
    if (-not $ok) { $bad += ('lead={0:X2} parse-failed' -f $lead); continue }
    for ($k = 0; $k -lt $trails.Count; $k++) {
        $c = $s[$k * 2]
        if ($c -ne [char]0x3F) { $pairs["$lead,$($trails[$k])"] = [string]$c }
    }
}

for ($b = 0x00; $b -le 0xFF; $b++) {
    if ($b -eq 0x00 -or $b -eq 0x41) { continue }
    $s = $enc.GetString([byte[]]@([byte]$b, [byte]0x00, [byte]0x41))
    if ($s.Length -eq 3 -and $s[1] -eq [char]0 -and $s[2] -eq [char]0x41) { $singles["$b"] = [string]$s[0] }
    elseif ($s.Length -eq 2) { }
    else { $bad += ('single={0:X2} unexpected-len={1}' -f $b, $s.Length) }
}

$obj = @{ pairs = $pairs; singles = $singles; bad = $bad }
$json = $obj | ConvertTo-Json -Compress -Depth 4
[System.IO.File]::WriteAllText('C:\Users\jiliy\Airline-CEO-Simulator\tools\recover\cp936_tables.json', $json, [System.Text.Encoding]::UTF8)
Write-Output ('pairs=' + $pairs.Count + ' singles=' + $singles.Count + ' bad=' + $bad.Count + ' jsonChars=' + $json.Length)
$bad | Select-Object -First 20 | ForEach-Object { Write-Output $_ }
