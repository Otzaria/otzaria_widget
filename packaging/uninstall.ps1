<#
.SYNOPSIS
  Removes the Otzaria Widget package (loose-file or MSIX). Does not touch Otzaria or its data.
  Removing the package deletes its LocalCache (and, if AppData virtualisation was active, provider.log + settings.json) and unpins its widgets.
  Files that were written to the real %LOCALAPPDATA%\OtzariaWidget (not redirected) stay; delete that folder by hand for a clean slate.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Get-Process OtzariaWidget -ErrorAction SilentlyContinue | Stop-Process -Force
$p = Get-AppxPackage 'Otzaria.Widget'
if (-not $p) { Write-Host 'Otzaria.Widget is not installed.'; return }
$p | Remove-AppxPackage
Write-Host "Removed $($p.PackageFullName)" -ForegroundColor Green
