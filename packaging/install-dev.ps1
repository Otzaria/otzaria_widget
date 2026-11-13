<#
.SYNOPSIS
  Registers the built layout (artifacts\layout) as a loose-file MSIX package. Needs Developer Mode, NO admin, no signing.

.DESCRIPTION
  Add-AppxPackage -Register points Windows at the layout folder, so the folder must stay in place while installed.
  Re-running after build.ps1 (new version each build) updates in place and keeps pinned widgets.
  Remove with packaging\uninstall.ps1.
#>
[CmdletBinding()]
param(
  [string]$Layout = ''
)
$ErrorActionPreference = 'Stop'
$pkgDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root   = Split-Path -Parent $pkgDir
if (-not $Layout) {
  $lt = Join-Path $root 'artifacts\layout.txt'
  $Layout = if (Test-Path $lt) { (Get-Content $lt -Raw).Trim() } else { Join-Path $root 'artifacts\layout' }
}
$manifest = Join-Path $Layout 'AppxManifest.xml'
if (-not (Test-Path $manifest)) { throw "No layout at $Layout - run packaging\build.ps1 first." }

# Developer Mode (loose-file registration needs it)
$dev = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue
if (-not $dev -or $dev.AllowDevelopmentWithoutDevLicense -ne 1) {
  Write-Warning 'Developer Mode appears to be OFF. Enable it: Settings > System > For developers > Developer Mode (one time, needs admin once).'
}

# Framework dependency (Windows App Runtime 1.8)
if (-not (Get-AppxPackage 'Microsoft.WindowsAppRuntime.1.8' -ErrorAction SilentlyContinue)) {
  Write-Warning 'Microsoft.WindowsAppRuntime.1.8 is not installed; registration will fail. Install it: winget install Microsoft.WindowsAppRuntime.1.8'
}

Get-Process OtzariaWidget -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host '== Add-AppxPackage -Register' -ForegroundColor Cyan
Add-AppxPackage -Register $manifest -ForceApplicationShutdown -ForceUpdateFromAnyVersion
$p = Get-AppxPackage 'Otzaria.Widget'
$p | Format-List Name, Version, PackageFullName, PackageFamilyName, InstallLocation, Status
if ($p) {
  # Windows redirects the app's %LOCALAPPDATA%\OtzariaWidget into the package LocalCache only when that folder does not already exist
  # in the real %LOCALAPPDATA% (an unpackaged dev run creates it): so the log is in one of these two places.
  Write-Host "Provider log: $env:LOCALAPPDATA\OtzariaWidget\provider.log"
  Write-Host "          or: $env:LOCALAPPDATA\Packages\$($p.PackageFamilyName)\LocalCache\Local\OtzariaWidget\provider.log"
  Write-Host 'Drive the provider without the board:  packaging/run-hostsim.ps1   (fake Widgets host inside the package identity)'

  Write-Host 'Open the Widgets board (Win+W) > "+" to add the widgets. Pinned widgets survive re-registration.' -ForegroundColor Green
}
