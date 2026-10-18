# Dump the .NET CP936 pair table as pure-ASCII lines: LEADHEX TRAILHEX CODEUNITS-HEX
$enc = [System.Text.Encoding]::GetEncoding(936)
$sb = New-Object System.Text.StringBuilder
for ($lead = 0x81; $lead -le 0xFE; $lead++) {
    for ($trail = 0x00; $trail -le 0xFF; $trail++) {
        $s = $enc.GetString([byte[]]@([byte]$lead, [byte]$trail))
        if ($s.Length -ne 1) { continue }
        $u = [int]$s[0]
        [void]$sb.Append(('{0:X2} {1:X2} {2:X4}' -f $lead, $trail, $u)).Append("`n")
    }
}
for ($b = 0x00; $b -le 0xFF; $b++) {
    $s = $enc.GetString([byte[]]@([byte]$b))
    if ($s.Length -ne 1) { continue }
    $u = [int]$s[0]
    [void]$sb.Append(('S {0:X2} {1:X4}' -f $b, $u)).Append("`n")
}
[System.IO.File]::WriteAllText('C:\Users\jiliy\Airline-CEO-Simulator\tools\recover\cp936_pairs.txt', $sb.ToString(), [System.Text.Encoding]::ASCII)
Write-Output ('lines=' + ($sb.ToString().Split("`n").Count))
