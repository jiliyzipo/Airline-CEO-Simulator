# Probe .NET CP936 decoder behavior for invalid sequences so we can replicate it in JS.
$enc = [System.Text.Encoding]::GetEncoding(936)
function Show($label, $bytes) {
  $s = $enc.GetString([byte[]]$bytes)
  $hex = ($bytes | ForEach-Object { $_.ToString('X2') }) -join ' '
  $codes = ($s.ToCharArray() | ForEach-Object { 'U+' + ([int]$_).ToString('X4') }) -join ' '
  Write-Output ("$label [$hex] -> chars=$($s.Length) codes=$codes")
}
Show 'lead+A'      @(0x81, 0x41)
Show 'lead+0A'     @(0x81, 0x0A)
Show 'lead+lead'   @(0x81, 0x81)
Show 'lead+FF'     @(0x81, 0xFF)
Show 'C0+41'       @(0xC0, 0x41)
Show 'E3+80+A'     @(0xE3, 0x80, 0x41)
Show 'E3+80+0A'    @(0xE3, 0x80, 0x0A)
Show 'lone 81'     @(0x81)
Show 'lone FF'     @(0xFF)
Show 'valid A1A1'  @(0xA1, 0xA1)
Show '80+41'       @(0x80, 0x41)
Show '80+80'       @(0x80, 0x80)
Show 'FF+FE'       @(0xFF, 0xFE)
Show 'A1+40'       @(0xA1, 0x40)
Show 'A1+41'       @(0xA1, 0x41)
Show 'E3 80 82 then 41' @(0xE3, 0x80, 0x82, 0x41)
