<#
.SYNOPSIS
  Builds a distributable, signed MSIX (artifacts\Otzaria.Widget_<version>_x64.msix) + the test certificate + installer script.

.DESCRIPTION
  1. runs build.ps1 (publish + layout + makepri; no registration) unless -SkipBuild
  2. makeappx pack artifacts\layout -> .msix
  3. signs with a SELF-SIGNED code-signing certificate (CN=OtzariaDev, created in CurrentUser\My on first use)
  4. writes artifacts\OtzariaDev.cer, copies packaging\install-msix.ps1 next to the msix, and copies the Windows App Runtime 1.8
     framework packages (from the NuGet cache) into artifacts\dependencies\ for machines that do not have it.

  IMPORTANT - installing a self-signed MSIX needs ADMIN once: Windows checks MSIX trust against the LocalMachine stores, so the
  .cer must be imported into LocalMachine\TrustedPeople (CurrentUser is NOT enough - verified, error 0x800B0109).
  Testers: right-click artifacts\install-msix.ps1 > Run with PowerShell as Administrator (it self-elevates).
  A real code-signing certificate (or the Microsoft Store) removes this step.
#>
[CmdletBinding()]
param(
  [string]$Version = '',
  [string]$Publisher = 'CN=OtzariaDev',
  [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$pkgDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root   = Split-Path -Parent $pkgDir
$kits   = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64'
$artifacts = Join-Path $root 'artifacts'
$layout = Join-Path $artifacts 'layout'   # build.ps1 may fall back to another folder if this one is locked (see artifacts\layout.txt)

if (-not $SkipBuild) {
  if ($Version) { & (Join-Path $pkgDir 'build.ps1') -Version $Version -Publisher $Publisher }
  else { & (Join-Path $pkgDir 'build.ps1') -Publisher $Publisher }
}
$Version = (Get-Content (Join-Path $artifacts 'version.txt') -Raw).Trim()
$layout = (Get-Content (Join-Path $artifacts 'layout.txt') -Raw).Trim()

$msix = Join-Path $artifacts "Otzaria.Widget_${Version}_x64.msix"
$cer  = Join-Path $artifacts 'OtzariaDev.cer'

# signing certificate (subject must equal the manifest Publisher exactly)
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $Publisher -and $_.NotAfter -gt (Get-Date).AddDays(30) } | Select-Object -First 1
if (-not $cert) {
  Write-Host '== creating self-signed code-signing certificate' -ForegroundColor Cyan
  $cert = New-SelfSignedCertificate -Type Custom -Subject $Publisher -KeyUsage DigitalSignature -FriendlyName 'Otzaria dev signing' `
          -CertStoreLocation Cert:\CurrentUser\My -KeyExportPolicy Exportable `
          -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') -NotAfter (Get-Date).AddYears(3)
}
Export-Certificate -Cert $cert -FilePath $cer -Force | Out-Null

Write-Host '== makeappx pack' -ForegroundColor Cyan
Get-ChildItem $artifacts -Filter 'Otzaria.Widget_*_x64.msix' -ErrorAction SilentlyContinue | Remove-Item -Force   # no stale versions next to the new one
# the manifest in the layout must not be packed together with leftovers
& "$kits\makeappx.exe" pack /d $layout /p $msix /o /nv | Out-Null
if ($LASTEXITCODE) { throw 'makeappx failed' }

Write-Host '== signtool sign' -ForegroundColor Cyan
& "$kits\signtool.exe" sign /fd SHA256 /sha1 $cert.Thumbprint $msix | Out-Null
if ($LASTEXITCODE) { throw 'signtool failed' }
Write-Host ('MSIX size: {0:N1} MB' -f ((Get-Item $msix).Length / 1MB)) -ForegroundColor DarkGray

# installer for testers + framework dependency packages
Copy-Item (Join-Path $pkgDir 'install-msix.ps1') (Join-Path $artifacts 'install-msix.ps1') -Force
$depDst = Join-Path $artifacts 'dependencies'
$depSrc = Get-ChildItem (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windowsappsdk.runtime') -Directory -ErrorAction SilentlyContinue |
          Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'tools\MSIX\win10-x64' } | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($depSrc) {
  New-Item -ItemType Directory -Force $depDst | Out-Null
  Copy-Item (Join-Path $depSrc '*.msix') $depDst -Force
  Write-Host "Windows App Runtime packages copied to $depDst" -ForegroundColor DarkGray
} else {
  Write-Warning 'Windows App Runtime 1.8 MSIX files not found in the NuGet cache; testers need: winget install Microsoft.WindowsAppRuntime.1.8'
}

Write-Host ''
Write-Host "MSIX:        $msix" -ForegroundColor Green
Write-Host "Certificate: $cer"
Write-Host "Installer:   $(Join-Path $artifacts 'install-msix.ps1')  (run as ADMINISTRATOR: imports the certificate into LocalMachine\TrustedPeople)"
