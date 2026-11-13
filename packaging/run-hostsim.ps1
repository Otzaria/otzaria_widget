<#
.SYNOPSIS
  Runs tools\HostSim (a fake Widgets host) INSIDE the registered Otzaria.Widget package identity and prints what the provider logged.

.DESCRIPTION
  An unpackaged process cannot talk to the provider's WinRT interfaces (0x80073D54, no package identity), so the harness is started with
  Invoke-CommandInDesktopPackage (Windows 10 1809+, the package must be registered: packaging\build.ps1 -Register).
  The harness CoCreates the provider (the packaged exe is started by COM), drives CreateWidget/Activate/OnActionInvoked/... with
  "hostsim-*" widget ids and writes its own HRESULT trace to -Out. The provider's log lines for the run are printed afterwards.

.PARAMETER Scenario
  full (default) | settings | keep
.PARAMETER WaitIdle
  Also wait up to 90 s and report whether the provider exits by itself (idle exit: 60 s with no widgets).
#>
[CmdletBinding()]
param(
  [ValidateSet('full', 'settings', 'keep')][string]$Scenario = 'full',
  [switch]$WaitIdle,
  [string]$Out = (Join-Path $env:TEMP 'otzaria-hostsim.txt')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$pkg = Get-AppxPackage 'Otzaria.Widget' | Select-Object -First 1
if (-not $pkg) { throw 'Otzaria.Widget is not registered. Run packaging\build.ps1 -Register first.' }
$appId = ([xml](Get-Content (Join-Path $pkg.InstallLocation 'AppxManifest.xml') -Raw -Encoding UTF8)).Package.Applications.Application.Id

Write-Host '== building tools\HostSim' -ForegroundColor Cyan
dotnet build (Join-Path $root 'tools\HostSim\HostSim.csproj') -c Release --nologo -v q | Out-Null
if ($LASTEXITCODE) { throw 'HostSim build failed' }
$dll = Join-Path $root 'tools\HostSim\bin\x64\Release\net10.0\HostSim.dll'
if (-not (Test-Path $dll)) { $dll = (Get-ChildItem (Join-Path $root 'tools\HostSim\bin') -Recurse -Filter HostSim.dll | Select-Object -First 1).FullName }
$dotnet = (Get-Command dotnet).Source

# provider log: the package may or may not have it redirected into LocalCache (see AppEnvironment.RealDataFolder)
$logs = @("$env:LOCALAPPDATA\Packages\$($pkg.PackageFamilyName)\LocalCache\Local\OtzariaWidget\provider.log", "$env:LOCALAPPDATA\OtzariaWidget\provider.log")
$before = @{}; foreach ($l in $logs) { $before[$l] = if (Test-Path $l) { (Get-Item $l).Length } else { 0 } }

Get-Process OtzariaWidget -ErrorAction SilentlyContinue | Stop-Process -Force   # start from a cold provider
Remove-Item $Out -ErrorAction SilentlyContinue

$hostArgs = "`"$dll`" `"$Out`" --scenario=$Scenario" + $(if ($WaitIdle) { ' --wait-idle' } else { '' }) + $(if ($Scenario -eq 'keep') { ' --keep-provider' } else { '' })
Write-Host "== Invoke-CommandInDesktopPackage ($($pkg.PackageFamilyName)!$appId)" -ForegroundColor Cyan
Invoke-CommandInDesktopPackage -PackageFamilyName $pkg.PackageFamilyName -AppId $appId -Command $dotnet -Args $hostArgs -PreventBreakaway

$deadline = (Get-Date).AddSeconds($(if ($WaitIdle) { 150 } else { 60 }))
while ((Get-Date) -lt $deadline) {
  if ((Test-Path $Out) -and (Select-String -Path $Out -Pattern ' HOSTSIM done$' -Quiet)) { break }
  Start-Sleep -Milliseconds 500
}
Write-Host "`n===== HostSim trace ($Out)" -ForegroundColor Yellow
if (Test-Path $Out) { Get-Content $Out -Encoding UTF8 } else { Write-Warning 'no trace written (harness did not start?)' }

foreach ($l in $logs) {
  if (-not (Test-Path $l)) { continue }
  $len = (Get-Item $l).Length
  if ($len -le $before[$l]) { continue }
  Write-Host "`n===== provider log lines added to $l" -ForegroundColor Yellow
  $fs = [IO.File]::Open($l, 'Open', 'Read', 'ReadWrite')
  try { $null = $fs.Seek($before[$l], 'Begin'); $sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8); $sr.ReadToEnd() } finally { $fs.Dispose() }
}
