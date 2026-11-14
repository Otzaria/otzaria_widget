<#
.SYNOPSIS
  Builds the Otzaria Widget package layout: dotnet publish -> artifacts\layout -> manifest + assets -> makepri.
  CLI only: no Visual Studio, no admin.

.DESCRIPTION
  Steps
    1. (re)generate assets with Pillow if the PNGs are missing           (packaging\tools\make-assets.py)
    2. dotnet publish src\OtzariaWidget.App -c Release -r win-x64        -> artifacts\layout
    3. copy Assets\, ProviderAssets\, Templates\ and write AppxManifest.xml (version substituted)
    4. makepri -> resources.pri
    5. -Register : Add-AppxPackage -Register (Developer Mode, no admin)  via install-dev.ps1

  Version: every build gets a NEW version (1.0.<days since 2026-01-01>.<minute of day>) so that
  Add-AppxPackage -Register updates the registered package in place and pinned widgets survive.
  Override with -Version 1.2.3.4.

.EXAMPLE
  .\packaging\build.ps1                 # layout only
  .\packaging\build.ps1 -Register       # layout + register for development
  .\packaging\make-msix.ps1             # distributable, signed .msix
#>
[CmdletBinding()]
param(
  [string]$Configuration = 'Release',
  [string]$Version = '',
  [string]$Publisher = 'CN=OtzariaDev',
  [string]$IdentityName = 'Otzaria.Widget',
  [string]$PublisherDisplayName = 'Otzaria Dev',
  [switch]$Register,
  [switch]$SkipPublish
)
$ErrorActionPreference = 'Stop'
$pkgDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$root     = Split-Path -Parent $pkgDir
# Windows SDK tools (makepri / makeappx / signtool): newest installed SDK that has them (no hard-coded SDK version)
$kits = @(Get-ChildItem (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin') -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^10\.' } | Sort-Object { [version]$_.Name } -Descending |
    ForEach-Object { Join-Path $_.FullName 'x64' } | Where-Object { Test-Path (Join-Path $_ 'makepri.exe') })[0]
if (-not $kits) { throw 'Windows SDK (makepri.exe / makeappx.exe / signtool.exe) not found: install the Windows 10/11 SDK' }
$artifacts = Join-Path $root 'artifacts'
$layout   = Join-Path $artifacts 'layout'
$project  = Join-Path $root 'src\OtzariaWidget.App\OtzariaWidget.App.csproj'

if (-not $Version) {
  $n = Get-Date
  $Version = '1.0.{0}.{1}' -f [int]($n - [datetime]'2026-01-01').TotalDays, [int]($n.TimeOfDay.TotalMinutes)
  # two builds inside the same minute (or a clock that went backwards) must still be an UPGRADE of the registered package
  $installed = Get-AppxPackage $IdentityName -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($installed -and ([version]$Version) -le ([version]$installed.Version)) {
    $iv = [version]$installed.Version
    $Version = '{0}.{1}.{2}.{3}' -f $iv.Major, $iv.Minor, $iv.Build, ($iv.Revision + 1)
  }
}
Write-Host "Package version: $Version" -ForegroundColor Cyan

# 0. assets --------------------------------------------------------------------------------------------------
if (-not (Test-Path (Join-Path $pkgDir 'Assets\StoreLogo.png')) -or -not (Test-Path (Join-Path $pkgDir 'ProviderAssets\Screenshot_Zmanim.png'))) {
  Write-Host '== generating assets (python + Pillow)' -ForegroundColor Cyan
  python -I (Join-Path $pkgDir 'tools\make-assets.py')
  if ($LASTEXITCODE) { throw 'make-assets.py failed (pip install pillow)' }
}

# 1. a running provider locks the exe; stop it ----------------------------------------------------------------
Get-Process OtzariaWidget -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

# 2. publish ----------------------------------------------------------------------------------------------------
if (-not $SkipPublish) {
  if (Test-Path $layout) {
    try { Remove-Item $layout -Recurse -Force -ErrorAction Stop }
    catch {
      # Something (a process started by Invoke-CommandInDesktopPackage / the Widgets host / an indexer) still holds a handle on the folder:
      # do not fail the build, publish next to it. artifacts\layout.txt tells install-dev.ps1 / make-msix.ps1 where the layout is.
      $layout = Join-Path $artifacts "layout-$Version"
      Write-Warning "artifacts\layout is locked; using $layout instead"
      if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
    }
  }
  Write-Host '== dotnet publish' -ForegroundColor Cyan
  dotnet publish $project -c $Configuration -r win-x64 -o $layout --nologo -v quiet
  if ($LASTEXITCODE) { throw 'dotnet publish failed' }
}
if (-not (Test-Path (Join-Path $layout 'OtzariaWidget.exe'))) { throw "OtzariaWidget.exe not found in $layout" }

# 3. assets + templates + manifest ------------------------------------------------------------------------------
foreach ($d in 'Assets', 'ProviderAssets') {
  $dst = Join-Path $layout $d
  if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
  Copy-Item (Join-Path $pkgDir $d) $dst -Recurse
}
# the csproj copies Templates\*.json next to the exe; make sure (belt and braces)
$tplSrc = Join-Path $root 'src\OtzariaWidget.App\Templates'
$tplDst = Join-Path $layout 'Templates'
New-Item -ItemType Directory -Force $tplDst | Out-Null
Copy-Item (Join-Path $tplSrc '*.json') $tplDst -Force

# licence notices travel with the binaries (LGPL Kosher.Zmanim, Apache/MIT SQLite stack, ...)
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') (Join-Path $layout 'THIRD-PARTY-NOTICES.md') -Force

$manifestOut = Join-Path $layout 'AppxManifest.xml'
(Get-Content (Join-Path $pkgDir 'Package.appxmanifest') -Raw -Encoding UTF8).Replace('@VERSION@', $Version).Replace('@PUBLISHER@', $Publisher).
  Replace('@IDENTITYNAME@', $IdentityName).Replace('@PUBLISHERDISPLAYNAME@', $PublisherDisplayName) |
  Set-Content $manifestOut -Encoding UTF8
$manifestText = Get-Content $manifestOut -Raw -Encoding UTF8
if ($manifestText -match '@[A-Z]+@') { throw "AppxManifest.xml still contains an unreplaced placeholder: $($Matches[0])" }
[xml]$null = $manifestText   # well-formedness check

# layout hygiene: no debug symbols, no crash-dump helper, no WPF localisation folders (cs, de, ...: only *.resources.dll inside; the UI is
# Hebrew/English and the neutral resources are in the main assemblies). Do this BEFORE makepri so resources.pri does not index them.
Get-ChildItem $layout -Recurse -Include '*.pdb' -File | Remove-Item -Force
Remove-Item (Join-Path $layout 'createdump.exe') -Force -ErrorAction SilentlyContinue
Get-ChildItem $layout -Directory | Where-Object { $_.Name -match '^[a-z]{2,3}(-[A-Za-z]{2,4})?$' -and
    -not (Get-ChildItem $_.FullName -Recurse -File | Where-Object { $_.Name -notlike '*.resources.dll' }) } | Remove-Item -Recurse -Force
$junk = Get-ChildItem $layout -Recurse -File | Where-Object { $_.Extension -in '.pdb', '.cs', '.csproj' -or $_.Name -like '*.Tests.*' -or $_.Name -like 'xunit*' }
if ($junk) { throw "layout contains test/build junk: $($junk.Name -join ', ')" }
$mb = [math]::Round((Get-ChildItem $layout -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "Layout size: $mb MB" -ForegroundColor DarkGray

# a previous makepri run may have left split resource packs behind
Get-ChildItem $layout -Filter 'resources*.pri' | Remove-Item -Force

# 4. resources.pri ------------------------------------------------------------------------------------------------
Write-Host '== makepri' -ForegroundColor Cyan
$cfg = Join-Path $artifacts 'priconfig.xml'
New-Item -ItemType Directory -Force $artifacts | Out-Null
& "$kits\makepri.exe" createconfig /cf $cfg /dq en-US /pv 10.0.0 /o | Out-Null
if ($LASTEXITCODE) { throw 'makepri createconfig failed' }
# one resources.pri only (the default config would split Language/Scale packs into extra files meant for app bundles)
(Get-Content $cfg -Raw) -replace '<autoResourcePackage[^>]*/>', '' | Set-Content $cfg -Encoding UTF8
& "$kits\makepri.exe" new /pr $layout /cf $cfg /mn $manifestOut /of (Join-Path $layout 'resources.pri') /o | Out-Null
if ($LASTEXITCODE) { throw 'makepri new failed' }

Set-Content (Join-Path $artifacts 'version.txt') $Version -Encoding ASCII
Set-Content (Join-Path $artifacts 'layout.txt') $layout -Encoding UTF8
Write-Host "Layout ready: $layout  (version $Version)" -ForegroundColor Green

# 5. register -------------------------------------------------------------------------------------------------------
if ($Register) { & (Join-Path $pkgDir 'install-dev.ps1') -Layout $layout }
