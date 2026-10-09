' ScreenClipMonitor - 后台静默启动（无控制台窗口）
' 用法: 双击此文件，或放入 shell:startup 开机自启
Dim sh, fso, dir, cmd
Set sh  = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
dir = fso.GetParentFolderName(WScript.ScriptFullName)
cmd = "powershell -NoProfile -STA -ExecutionPolicy Bypass -File """ & dir & "\ScreenClipMonitor.ps1"""
sh.Run cmd, 0, False
