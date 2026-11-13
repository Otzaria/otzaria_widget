<#
 Registers a throwaway otzaria:// protocol handler under HKCU (no admin) that appends the received URL to
 %TEMP%\otzaria-deeplink.log - so you can verify Action.OpenUrl with a custom scheme from the Widgets Board.
 Remove with:  .\register-test-scheme.ps1 -Remove
#>
param([switch]$Remove)
$key = 'HKCU:\Software\Classes\otzaria'
if ($Remove) { Remove-Item $key -Recurse -Force -ErrorAction SilentlyContinue; Write-Host 'removed'; return }
New-Item "$key\shell\open\command" -Force | Out-Null
Set-ItemProperty $key -Name '(default)' -Value 'URL:Otzaria Protocol'
Set-ItemProperty $key -Name 'URL Protocol' -Value ''
$cmd = 'cmd.exe /c echo %date% %time% %1 >> "%TEMP%\otzaria-deeplink.log"'
Set-ItemProperty "$key\shell\open\command" -Name '(default)' -Value $cmd
Write-Host "registered. Click 'Open in Otzaria' on the widget, then: Get-Content $env:TEMP\otzaria-deeplink.log"
