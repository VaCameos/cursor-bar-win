$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $here "CursorBar.exe"
if (-not (Test-Path $exe)) {
    Write-Error "CursorBar.exe not found next to install.ps1"
}

$dest = Join-Path $env:LOCALAPPDATA "Programs\CursorBar"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item $exe (Join-Path $dest "CursorBar.exe") -Force

$programs = [Environment]::GetFolderPath("Programs")
$shortcutPath = Join-Path $programs "Cursor Bar.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $dest "CursorBar.exe"
$shortcut.WorkingDirectory = $dest
$shortcut.IconLocation = Join-Path $dest "CursorBar.exe"
$shortcut.Description = "Cursor Bar"
$shortcut.Save()

Start-Process (Join-Path $dest "CursorBar.exe")
Write-Host "Installed to $dest"
Write-Host "Start menu shortcut: $shortcutPath"
