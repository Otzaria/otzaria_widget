<#
.SYNOPSIS
  Build + lay out + makepri + (register | pack+sign) the Otzaria Widget Spike. CLI only, no Visual Studio, no admin.

.EXAMPLE
  .\build.ps1                 # publish, build layout, makepri, Add-AppxPackage -Register (Developer Mode, loose files)
  .\build.ps1 -NoRegister     # only build the layout
  .\build.ps1 -Pack           # also makeappx pack + sign with a self-signed cert -> dist\*.msix + dist\*.cer
  .\build.ps1 -Pack -NoRegister
#>
[CmdletBinding()]
param(
  [string]$Configuration = 'Release',
  [string]$Version = '',   # default: 0.1.<days since 2026-01-01>.<minute of day>  (a new version each build so Add-AppxPackage -Register UPDATES in place and keeps pinned widgets)
  [string]$Publisher = 'CN=OtzariaDev',
  [switch]$NoRegister,
  [switch]$Pack
)
$ErrorActionPreference = 'Stop'
$here   = Split-Path -Parent $MyInvocation.MyCommand.Path
$kits   = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64'
$layout = Join-Path $here 'layout'
$dist   = Join-Path $here 'dist'
$pkgName = 'Otzaria.WidgetSpike'
if (-not $Version) { $n = Get-Date; $Version = '0.1.{0}.{1}' -f [int]($n - [datetime]'2026-01-01').TotalDays, [int]($n.TimeOfDay.TotalMinutes) }
Write-Host "Package version: $Version"

Push-Location $here
try {
  # 0. Assets (placeholder PNGs) -------------------------------------------------------------------------
  if (-not (Test-Path 'Assets\StoreLogo.png') -or -not (Test-Path 'ProviderAssets\Screenshot.png')) {
    Write-Host '== generating placeholder assets' -ForegroundColor Cyan
    python tools\make-assets.py
  }

  # 1. Stop a running provider + wipe layout (a running exe locks files) ----------------------------------
  Get-Process WidgetSpike -ErrorAction SilentlyContinue | Stop-Process -Force
  if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }

  # 2. dotnet publish straight into the layout (x64, self-contained .NET, framework-dependent WinAppSDK, no trim/AOT)
  Write-Host '== dotnet publish' -ForegroundColor Cyan
  dotnet publish WidgetSpike.csproj -c $Configuration -r win-x64 -o $layout
  if ($LASTEXITCODE) { throw 'dotnet publish failed' }

  # 3. assets + manifest -------------------------------------------------------------------------------
  Copy-Item Assets         (Join-Path $layout 'Assets')         -Recurse
  Copy-Item ProviderAssets (Join-Path $layout 'ProviderAssets') -Recurse
  (Get-Content Package.appxmanifest -Raw -Encoding UTF8).Replace('@VERSION@', $Version).Replace('@PUBLISHER@', $Publisher) |
    Set-Content (Join-Path $layout 'AppxManifest.xml') -Encoding UTF8
  [xml]$null = Get-Content (Join-Path $layout 'AppxManifest.xml') -Raw   # well-formedness check

  # 4. resources.pri (needed so Assets\x.png resolves with scale/lang qualifiers & the widget host can find screenshots)
  Write-Host '== makepri' -ForegroundColor Cyan
  $cfg = Join-Path $here 'obj\priconfig.xml'
  New-Item -ItemType Directory -Force (Split-Path $cfg) | Out-Null
  & "$kits\makepri.exe" createconfig /cf $cfg /dq en-US /pv 10.0.0 /o | Out-Null
  if ($LASTEXITCODE) { throw 'makepri createconfig failed' }
  & "$kits\makepri.exe" new /pr $layout /cf $cfg /mn (Join-Path $layout 'AppxManifest.xml') /of (Join-Path $layout 'resources.pri') /o | Out-Null
  if ($LASTEXITCODE) { throw 'makepri new failed' }

  # 5a. Developer-Mode loose-file registration (no admin, no signing) ------------------------------------------
  if (-not $NoRegister -and -not $Pack) {
    Write-Host '== Add-AppxPackage -Register' -ForegroundColor Cyan
    Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml') -ForceApplicationShutdown -ForceUpdateFromAnyVersion
    Get-AppxPackage $pkgName | Format-List Name, PackageFullName, PackageFamilyName, InstallLocation, Status
  }

  # 5b. Signed MSIX (distribution path) --------------------------------------------------------------------
  if ($Pack) {
    Write-Host '== makeappx pack + signtool sign' -ForegroundColor Cyan
    New-Item -ItemType Directory -Force $dist | Out-Null
    $msix = Join-Path $dist "$pkgName`_$Version`_x64.msix"
    $cer  = Join-Path $dist 'OtzariaDev.cer'
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $Publisher -and $_.NotAfter -gt (Get-Date) } | Select-Object -First 1
    if (-not $cert) {
      $cert = New-SelfSignedCertificate -Type Custom -Subject $Publisher -KeyUsage DigitalSignature -FriendlyName 'Otzaria dev signing' `
              -CertStoreLocation Cert:\CurrentUser\My -KeyExportPolicy Exportable `
              -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') -NotAfter (Get-Date).AddYears(3)
    }
    Export-Certificate -Cert $cert -FilePath $cer -Force | Out-Null
    # makeappx must not see our own layout\*.msix etc.; pack the layout dir
    & "$kits\makeappx.exe" pack /d $layout /p $msix /o /nv
    if ($LASTEXITCODE) { throw 'makeappx failed' }
    & "$kits\signtool.exe" sign /fd SHA256 /sha1 $cert.Thumbprint $msix
    if ($LASTEXITCODE) { throw 'signtool failed' }
    Write-Host "Signed package: $msix`nCertificate:    $cer" -ForegroundColor Green
    Write-Host 'Install (needs the .cer trusted in LocalMachine\TrustedPeople = admin, see docs): Add-AppxPackage <msix>'
  }
  Write-Host 'DONE' -ForegroundColor Green
}
finally { Pop-Location }
