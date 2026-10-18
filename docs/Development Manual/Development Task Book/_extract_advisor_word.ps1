$ErrorActionPreference = 'Stop'
$path = 'c:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Development Task Book\Airline_CEO_顾问开局引导线_v1.1.doc'
$out = 'c:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Development Task Book\_advisor_guide_v1.1_extracted.txt'
$word = New-Object -ComObject Word.Application
$word.Visible = $false
try {
  $doc = $word.Documents.Open($path, $false, $true)
  $text = $doc.Content.Text
  $doc.Close($false)
  [System.IO.File]::WriteAllText($out, $text, [System.Text.Encoding]::UTF8)
  Write-Output ("OK chars=" + $text.Length)
  Write-Output $text.Substring(0, [Math]::Min(3000, $text.Length))
} finally {
  $word.Quit()
  [System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null
}
