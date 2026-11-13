<#
.SYNOPSIS  Remove the Otzaria Widget Spike (stops the provider, unregisters the package, keeps the source tree).
           Pass -CleanLogs to also delete %LOCALAPPDATA%\OtzariaWidgetSpike and the package's redirected copy.
#>
param([switch]$CleanLogs)
Get-Process WidgetSpike -ErrorAction SilentlyContinue | Stop-Process -Force
$p = Get-AppxPackage Otzaria.WidgetSpike
if ($p) { $p | Remove-AppxPackage; Write-Host "Removed $($p.PackageFullName)" } else { Write-Host 'Not registered.' }
if ($CleanLogs) {
  Remove-Item "$env:LOCALAPPDATA\OtzariaWidgetSpike" -Recurse -Force -ErrorAction SilentlyContinue
}
