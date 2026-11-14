<#
.SYNOPSIS
  Builds a distributable, signed MSIX (artifacts\Otzaria.Widget_<version>_x64.msix) + the test certificate + installer script,
  or (-Store) an UNSIGNED MSIX for upload to Microsoft Partner Center (artifacts\store\).

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

  -Store: Microsoft Store package. Needs the three values from Partner Center > the app > Product management > Product identity:
    -IdentityName          Package/Identity/Name                    e.g. 12345Publisher.OtzariaWidget
    -Publisher             Package/Identity/Publisher               e.g. CN=XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX
    -PublisherDisplayName  Package/Properties/PublisherDisplayName  e.g. Your Publisher Name
  Differences from the dev flow: no signtool, no self-signed certificate, no .cer / install-msix.ps1 / dependencies\ (the Store signs the
  package and resolves Microsoft.WindowsAppRuntime.1.8 itself). Version must have a non-zero first segment and a 4th segment of 0 (Store
  rule) and be higher than every version already submitted; the default is yyyy.MMdd.HHmm.0 (same scheme as the GitHub release workflow).
  Without all three identity values the script still runs, with clearly marked PLACEHOLDER values, a loud warning and a
  *_PLACEHOLDER-IDENTITY.msix file name: such a package is rejected by Partner Center.

.EXAMPLE
  .\packaging\make-msix.ps1 -Store -IdentityName 12345Publisher.OtzariaWidget -Publisher 'CN=XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX' -PublisherDisplayName 'Your Publisher Name'
#>
[CmdletBinding()]
param(
  [string]$Version = '',
  [string]$Publisher = '',
  [string]$IdentityName = '',
  [string]$PublisherDisplayName = '',
  [switch]$Store,
  [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$pkgDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root   = Split-Path -Parent $pkgDir
# Windows SDK tools (makepri / makeappx / signtool): newest installed SDK that has them (no hard-coded SDK version)
$kits = @(Get-ChildItem (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin') -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^10\.' } | Sort-Object { [version]$_.Name } -Descending |
    ForEach-Object { Join-Path $_.FullName 'x64' } | Where-Object { Test-Path (Join-Path $_ 'makeappx.exe') })[0]
if (-not $kits) { throw 'Windows SDK (makepri.exe / makeappx.exe / signtool.exe) not found: install the Windows 10/11 SDK' }
$artifacts = Join-Path $root 'artifacts'
$layout = Join-Path $artifacts 'layout'   # build.ps1 may fall back to another folder if this one is locked (see artifacts\layout.txt)

# ---- identity values ----------------------------------------------------------------------------------------------------------
$placeholderStore = $false
$missing = @()
if ($Store) {
  if ($SkipBuild) { throw '-Store cannot be combined with -SkipBuild: the manifest in the existing layout carries the dev identity' }
  if (-not $IdentityName)         { $missing += 'IdentityName';         $IdentityName         = 'REPLACE.WithPartnerCenterIdentityName' }
  if (-not $Publisher)            { $missing += 'Publisher';            $Publisher            = 'CN=REPLACE-WITH-PARTNER-CENTER-PUBLISHER-ID' }
  if (-not $PublisherDisplayName) { $missing += 'PublisherDisplayName'; $PublisherDisplayName = 'REPLACE WITH PARTNER CENTER PUBLISHER DISPLAY NAME' }
  $placeholderStore = $missing.Count -gt 0
  if ($IdentityName -notmatch '^[A-Za-z0-9][A-Za-z0-9.\-]{2,49}$') { throw "IdentityName '$IdentityName' is not a valid package identity name (3-50 chars: letters, digits, '.', '-')" }
  if ($Publisher -notmatch '^CN=.+') { throw "Publisher '$Publisher' must be the Partner Center 'Package/Identity/Publisher' value, starting with CN=" }
  if (("$IdentityName$Publisher$PublisherDisplayName") -match '[<>&"]') { throw 'identity values must not contain < > & " (XML special characters)' }
  if (-not $Version) { $Version = (Get-Date).ToString('yyyy.MMdd.HHmm') + '.0' }
  # Store rules: 4 numeric segments <= 65535, first segment non-zero, 4th segment 0 (reserved by the Store)
  if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw "Version '$Version' must be a.b.c.d" }
  $seg = @($Version.Split('.') | ForEach-Object { [int64]$_ })
  if ($seg | Where-Object { $_ -gt 65535 }) { throw "Version '$Version': every segment must be <= 65535" }
  if ($seg[0] -eq 0) { throw "Version '$Version': the first segment must not be 0 (Store rule)" }
  if ($seg[3] -ne 0) { throw "Version '$Version': the 4th segment must be 0 (reserved for the Store)" }
} else {
  if (-not $Publisher)            { $Publisher = 'CN=OtzariaDev' }
  if (-not $IdentityName)         { $IdentityName = 'Otzaria.Widget' }
  if (-not $PublisherDisplayName) { $PublisherDisplayName = 'Otzaria Dev' }
}

if (-not $SkipBuild) {
  $bargs = @{ Publisher = $Publisher; IdentityName = $IdentityName; PublisherDisplayName = $PublisherDisplayName }
  if ($Version) { $bargs.Version = $Version }
  & (Join-Path $pkgDir 'build.ps1') @bargs
}
$Version = (Get-Content (Join-Path $artifacts 'version.txt') -Raw).Trim()
$layout = (Get-Content (Join-Path $artifacts 'layout.txt') -Raw).Trim()

if ($Store) {
  # Store package: pack only. No signing (Partner Center signs it), no certificate, no installer, no dependency packages.
  $storeDir = Join-Path $artifacts 'store'
  New-Item -ItemType Directory -Force $storeDir | Out-Null
  Get-ChildItem $storeDir -Filter '*_PLACEHOLDER-IDENTITY.msix' -ErrorAction SilentlyContinue | Remove-Item -Force
  $suffix = if ($placeholderStore) { '_PLACEHOLDER-IDENTITY' } else { '' }
  $msix = Join-Path $storeDir "Otzaria.Widget_${Version}_x64$suffix.msix"
  Write-Host '== makeappx pack (Store, unsigned)' -ForegroundColor Cyan
  & "$kits\makeappx.exe" pack /d $layout /p $msix /o /nv | Out-Null
  if ($LASTEXITCODE) { throw 'makeappx failed' }
  Write-Host ('MSIX size: {0:N1} MB' -f ((Get-Item $msix).Length / 1MB)) -ForegroundColor DarkGray
  Write-Host ''
  Write-Host "Store MSIX (UNSIGNED): $msix" -ForegroundColor Green
  Write-Host "  Identity Name:        $IdentityName"
  Write-Host "  Identity Publisher:   $Publisher"
  Write-Host "  PublisherDisplayName: $PublisherDisplayName"
  Write-Host "  Version:              $Version  (must be higher than every version already submitted)"
  if ($placeholderStore) {
    Write-Host ''
    Write-Warning ('PLACEHOLDER IDENTITY, not supplied: ' + ($missing -join ', '))
    Write-Host '*****************************************************************************************' -ForegroundColor Yellow
    Write-Host '* THIS PACKAGE CANNOT BE UPLOADED TO PARTNER CENTER: its identity is a placeholder.     *' -ForegroundColor Yellow
    Write-Host '* Re-run with the values from Partner Center > Product management > Product identity:  *' -ForegroundColor Yellow
    Write-Host '*   -IdentityName <Identity/Name> -Publisher <Identity/Publisher>                        *' -ForegroundColor Yellow
    Write-Host '*   -PublisherDisplayName <PublisherDisplayName>                                         *' -ForegroundColor Yellow
    Write-Host '*****************************************************************************************' -ForegroundColor Yellow
  }
  return
}

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
