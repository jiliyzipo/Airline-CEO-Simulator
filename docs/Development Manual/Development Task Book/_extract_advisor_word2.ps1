$ErrorActionPreference = 'Continue'
$path = 'c:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Development Task Book\Airline_CEO_顾问开局引导线_v1.1.doc'
$outTxt = 'c:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Development Task Book\_advisor_guide_v1.1_extracted.txt'
$outDocx = 'c:\Users\jiliy\Airline-CEO-Simulator\docs\Development Manual\Development Task Book\_advisor_guide_v1.1.docx'

$word = $null
$doc = $null
try {
  $word = New-Object -ComObject Word.Application
  $word.Visible = $false
  $word.DisplayAlerts = 0
  $doc = $word.Documents.Open($path, $false, $true)
  Start-Sleep -Milliseconds 500
  $text = $doc.Content.Text
  [System.IO.File]::WriteAllText($outTxt, $text, [System.Text.UTF8Encoding]::new($false))
  # Also save as docx for later
  $wdFormatXMLDocument = 12
  $doc.SaveAs2([ref]$outDocx, [ref]$wdFormatXMLDocument)
  Write-Output ("OK len=" + $text.Length)
} catch {
  Write-Output ("ERR " + $_.Exception.Message)
} finally {
  if ($doc -ne $null) { try { $doc.Close($false) } catch {} }
  if ($word -ne $null) { try { $word.Quit() } catch {} }
}
