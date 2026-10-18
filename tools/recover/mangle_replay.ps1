# Mangle the replayed pristine content with the same .NET CP936 decoder that corrupted the file.
$src = 'C:\Users\jiliy\Airline-CEO-Simulator\tools\recover\replay_out.cs'
$dst = 'C:\Users\jiliy\Airline-CEO-Simulator\tools\recover\replay_mangled.txt'
$rep = [System.IO.File]::ReadAllText($src, [System.Text.Encoding]::UTF8)
$enc = [System.Text.Encoding]::GetEncoding(936)
$m = $enc.GetString([System.Text.Encoding]::UTF8.GetBytes($rep))
[System.IO.File]::WriteAllText($dst, $m, [System.Text.Encoding]::UTF8)
Write-Output ('srcChars=' + $rep.Length + ' mangledChars=' + $m.Length)
