# Dump the exact .NET CP936 decoder table (2-byte sequences that decode to exactly one char).
$enc = [System.Text.Encoding]::GetEncoding(936)
$sb = New-Object System.Text.StringBuilder
$count = 0
for ($lead = 0x81; $lead -le 0xFE; $lead++) {
  for ($trail = 0x00; $trail -le 0xFF; $trail++) {
    $s = $enc.GetString([byte[]]@($lead, $trail))
    if ($s.Length -eq 1) {
      $ch = [int][char]$s[0]
      if ($ch -ne 0x3F -and $ch -ne 0xFFFD) {
        [void]$sb.Append("$lead,$trail`t$ch`n")
        $count++
      }
    }
  }
}
$dst = 'C:\Users\jiliy\Airline-CEO-Simulator\tools\recover\cp936_pairs_full.txt'
[System.IO.File]::WriteAllText($dst, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Write-Output ("pairsFull=$count")
