; Inno Setup script for Otzaria Widget
; Bundles the signed MSIX + certificate + runtime dependencies into a single EXE.
; The EXE requests admin, imports the cert, installs dependencies, then the MSIX.
;
; Preprocessor defines (passed via /D on the ISCC command line):
;   AppVersion  - e.g. 2026.1113.1430
;   MsixPath    - full path to the .msix file
;   MsixName    - filename only, e.g. Otzaria.Widget_2026.1113.1430_x64.msix
;   CerPath     - full path to OtzariaDev.cer
;   DepsPath    - full path to dependencies folder (may contain 0 files)
;   IconPath    - full path to app.ico
;   OutputDir   - where the setup EXE is written

[Setup]
AppName=Otzaria Widget
AppVersion={#AppVersion}
AppPublisher=Otzaria Dev
DefaultDirName={tmp}\OtzariaWidgetInstall
CreateAppDir=yes
Uninstallable=no
OutputBaseFilename=OtzariaWidget-{#AppVersion}-x64-setup
OutputDir={#OutputDir}
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
WizardStyle=modern
SetupIconFile={#IconPath}

[Languages]
Name: "hebrew"; MessagesFile: "compiler:Languages\Hebrew.isl"

[Files]
Source: "{#MsixPath}"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall
Source: "{#CerPath}";  DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall
Source: "{#DepsPath}\*.msix"; DestDir: "{tmp}\deps"; Flags: ignoreversion deleteafterinstall skipifsourcedoesntexist

[Run]
; 1. Import signing certificate into LocalMachine\TrustedPeople
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Import-Certificate -FilePath '{tmp}\OtzariaDev.cer' -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null"""; \
  StatusMsg: "מייבא תעודת חתימה..."; \
  Flags: runhidden

; 2. Install Windows App Runtime framework packages (if bundled)
; Inno escaping: {{ = literal {   ;   } = literal }
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""$d = '{tmp}\deps'; if (Test-Path $d) {{ Get-ChildItem $d -Filter *.msix | ForEach-Object {{ try {{ Add-AppxPackage -Path $_.FullName -ErrorAction Stop } catch {{ } } } }"""; \
  StatusMsg: "מתקין Windows App Runtime..."; \
  Flags: runhidden

; 3. Install the widget MSIX
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Get-Process OtzariaWidget -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep -Milliseconds 500; Add-AppxPackage -Path '{tmp}\{#MsixName}' -ForceApplicationShutdown"""; \
  StatusMsg: "מתקין ווידג'ט אוצריא..."; \
  Flags: runhidden

[Messages]
hebrew.SetupWindowTitle=התקנת ווידג'ט אוצריא
hebrew.WelcomeLabel1=ברוכים הבאים להתקנת ווידג'ט אוצריא
hebrew.WelcomeLabel2=האשף יתקין את ווידג'ט אוצריא במחשב שלך.%n%nהתוכנה תתקין תעודת חתימה, את סביבת הריצה של Windows App SDK, ואת הווידג'ט עצמו.%n%nלאחר ההתקנה, פתח את לוח הווידג'טים (Win+W) ולחץ "+" כדי להוסיף את ווידג'טי אוצריא.
hebrew.FinishedLabel=ההתקנה הושלמה בהצלחה.%n%nפתח את לוח הווידג'טים (Win+W) ולחץ "+" כדי להוסיף את ווידג'טי אוצריא.
