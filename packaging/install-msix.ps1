<#
.SYNOPSIS
  Installs the signed Otzaria Widget MSIX on a tester machine. REQUIRES ADMINISTRATOR (self-signs: imports the
  certificate into LocalMachine\TrustedPeople - Windows ignores CurrentUser for MSIX trust).
  Copied next to the .msix / OtzariaDev.cer by make-msix.ps1; run it from that folder.

.EXAMPLE
  Right-click > Run with PowerShell (it re-launches itself elevated), or from an elevated shell:
  .\install-msix.ps1
#>
[CmdletBinding()]
param(
  [string]$Msix = '',
  [switch]$SkipDependencies
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

# self-elevate
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
  Write-Host 'Administrator rights are required (certificate import into LocalMachine\TrustedPeople). Re-launching elevated...' -ForegroundColor Yellow
  $argsList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$($MyInvocation.MyCommand.Path)`"")
  if ($Msix) { $argsList += @('-Msix', "`"$Msix`"") }
  if ($SkipDependencies) { $argsList += '-SkipDependencies' }
  Start-Process powershell -Verb RunAs -ArgumentList $argsList -Wait
  return
}

if (-not $Msix) { $Msix = (Get-ChildItem $here -Filter 'Otzaria.Widget_*_x64.msix' | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName }
if (-not $Msix -or -not (Test-Path $Msix)) { throw 'Otzaria.Widget_*.msix not found next to this script.' }
$cer = Join-Path $here 'OtzariaDev.cer'
if (-not (Test-Path $cer)) { throw 'OtzariaDev.cer not found next to this script.' }

Write-Host '== trusting the signing certificate (LocalMachine\TrustedPeople)' -ForegroundColor Cyan
Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null

# Windows App Runtime 1.8 framework (declared as a package dependency)
# MinVersion must equal the PackageDependency in Package.appxmanifest
$minRuntime = [version]'8000.806.2252.0'
$haveRuntime = Get-AppxPackage 'Microsoft.WindowsAppRuntime.1.8' -ErrorAction SilentlyContinue | Where-Object { $_.Architecture -eq 'X64' -and [version]$_.Version -ge $minRuntime }
if (-not $SkipDependencies -and -not $haveRuntime) {
  $dep = Join-Path $here 'dependencies'
  if (Test-Path $dep) {
    Write-Host '== installing Windows App Runtime 1.8' -ForegroundColor Cyan
    foreach ($n in 'Microsoft.WindowsAppRuntime.1.8.msix', 'Microsoft.WindowsAppRuntime.Main.1.8.msix', 'Microsoft.WindowsAppRuntime.Singleton.1.8.msix', 'Microsoft.WindowsAppRuntime.DDLM.1.8.msix') {
      $f = Join-Path $dep $n
      if (Test-Path $f) { try { Add-AppxPackage -Path $f -ErrorAction Stop } catch { Write-Warning "$n : $($_.Exception.Message)" } }
    }
  } else {
    Write-Warning 'dependencies\ folder missing. Install the runtime manually: winget install Microsoft.WindowsAppRuntime.1.8'
  }
}

Write-Host '== installing Otzaria Widget' -ForegroundColor Cyan
Get-Process OtzariaWidget -ErrorAction SilentlyContinue | Stop-Process -Force
Add-AppxPackage -Path $Msix -ForceApplicationShutdown
Get-AppxPackage 'Otzaria.Widget' | Format-List Name, Version, Status
Write-Host 'Done. Open the Widgets board (Win+W) > "+" to add the Otzaria widgets.' -ForegroundColor Green
if ($Host.Name -eq 'ConsoleHost') { Read-Host 'Press Enter to close' | Out-Null }
