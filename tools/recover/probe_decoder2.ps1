$enc = [System.Text.Encoding]::GetEncoding(936)
function Show($label, $bytes) {
  $s = $enc.GetString([byte[]]$bytes)
  $hex = ($bytes | ForEach-Object { $_.ToString('X2') }) -join ' '
  $codes = ($s.ToCharArray() | ForEach-Object { 'U+' + ([int]$_).ToString('X4') }) -join ' '
  Write-Output ("$label [$hex] -> n=$($s.Length) $codes")
}
# does an invalid trail get consumed?
Show '81+20'  @(0x81, 0x20)
Show '81+09'  @(0x81, 0x09)
Show '81+3B'  @(0x81, 0x3B)
Show '81+7F'  @(0x81, 0x7F)
Show '81+00'  @(0x81, 0x00)
Show '81+2C'  @(0x81, 0x2C)
Show '82+0A+41' @(0x82, 0x0A, 0x41)
Show 'C0+0A'  @(0xC0, 0x0A)
Show 'C0+20+41' @(0xC0, 0x20, 0x41)
Show 'A1+0A'  @(0xA1, 0x0A)
Show 'A1+20'  @(0xA1, 0x20)
Show 'FE+0A'  @(0xFE, 0x0A)
Show 'FF+41'  @(0xFF, 0x41)
Show 'FF+0A'  @(0xFF, 0x0A)
Show '80+0A'  @(0x80, 0x0A)
Show '79+41'  @(0x79, 0x41)
