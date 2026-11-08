; Cosmic Crusade installer (Inno Setup 6). Built by build\publish.ps1 (or publish.bat at the root), which passes
; /DAppVersion from Directory.Build.props and compiles from the self-contained publish it has just made in artifacts\app\.
; Same approach as Throng's installer. Per user, no admin rights.
;
;   App:        %LocalAppData%\Programs\Cosmic Crusade\ (fixed, so [InstallDelete] can safely start the folder clean).
;               It includes data\ (cards, art, strips, layout, pools).
;   Shortcuts:  Start menu (the game + the LAN server); desktop as an option, ticked (it's a game)
;   Licence:    MIT for the software, game content all rights reserved: shown as an information page (no acceptance)
;   Kept:       %AppData%\CosmicCrusade (your name and server address) is never removed by uninstall
;   Network:    online play only makes outgoing connections to the relay, so the game needs no firewall rule. The LAN
;               server (CosmicCrusade-LanServer.exe) listens on port 5080; Windows asks the first time it's started.
;
; AppId never changes: it is how a newer setup finds and upgrades an older install.

#define PublishedExe AddBackslash(SourcePath) + "..\artifacts\app\CosmicCrusade.exe"

; build\publish.ps1 passes /DAppVersion. Compiled by hand (Inno Setup Compiler after the Visual Studio publish
; profile), the version is read from the published exe instead.
#ifndef AppVersion
  #if FileExists(PublishedExe)
    #define VerMajor 0
    #define VerMinor 0
    #define VerRev 0
    #define VerBuild 0
    #expr GetVersionComponents(PublishedExe, VerMajor, VerMinor, VerRev, VerBuild)
    #define AppVersion Str(VerMajor) + "." + Str(VerMinor) + "." + Str(VerRev)
  #else
    #error artifacts\app\CosmicCrusade.exe not found: publish the app first (publish.bat, or the Release-win-x64 publish profile).
  #endif
#endif

#define AppName "Cosmic Crusade"
#define AppExe "CosmicCrusade.exe"

[Setup]
AppId={{5A56303A-43B9-4EFB-928C-D34F05229610}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Gavin Nicholls
AppCopyright=Copyright (c) 2026 Gavin Nicholls (software MIT; game content all rights reserved)
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} setup
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
InfoBeforeFile=..\LICENSE
SetupIconFile=..\src\CosmicCrusade.App\Assets\CosmicCrusade.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName} {#AppVersion}
OutputBaseFilename=CosmicCrusade-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no
UsePreviousTasks=yes
; The game running is checked in [Code]: it's never closed for the player (they may be mid-game, maybe online)
CloseApplications=no
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"

[InstallDelete]
; The app folder holds nothing but the app: start clean (an older self-contained build may leave dlls or card art
; that a new one doesn't use)
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "..\artifacts\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Play Cosmic Crusade against the AI or online"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Play Cosmic Crusade against the AI or online"; Tasks: desktopicon
Name: "{autoprograms}\{#AppName} How to Play"; Filename: "{app}\docs\HOW-TO-PLAY.md"; Comment: "Learn to play, plus the rulebook and glossary"
#if FileExists(AddBackslash(SourcePath) + "..\artifacts\app\CosmicCrusade-LanServer.exe")
Name: "{autoprograms}\{#AppName} LAN Server"; Filename: "{app}\CosmicCrusade-LanServer.exe"; Comment: "Host Cosmic Crusade games on your home network (leave its window open while you play)"
#endif

[Run]
Filename: "{app}\{#AppExe}"; Description: "Play {#AppName} now"; Flags: postinstall nowait skipifsilent

[UninstallDelete]
; Only the app folder. %AppData%\CosmicCrusade (name, server address) is kept for a reinstall.
Type: filesandordirs; Name: "{app}"

[Code]
function IsProcessRunning(const FileName: String): Boolean;
var
  Locator, Service, Items: Variant;
begin
  Result := False;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Items := Service.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = "' + FileName + '"');
    Result := Items.Count > 0;
  except
    Result := False; { can't tell: carry on (a locked file still makes setup ask) }
  end;
end;

{ CosmicCrusade.exe holds the files this setup replaces: ask the player to close it (never closed for them) }
function GameClosed(const What: String): Boolean;
begin
  Result := True;
  while IsProcessRunning('{#AppExe}') or IsProcessRunning('CosmicCrusade-LanServer.exe') do
  begin
    if MsgBox('Please close Cosmic Crusade (and the LAN Server window, if it''s open) before ' + What + '.' + #13#10#13#10 +
              'Then choose Retry.', mbError, MB_RETRYCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

function InitializeSetup: Boolean;
begin
  if WizardSilent then
    Result := not IsProcessRunning('{#AppExe}') and not IsProcessRunning('CosmicCrusade-LanServer.exe')
  else
    Result := GameClosed('installing');
end;

function InitializeUninstall: Boolean;
begin
  if UninstallSilent then
    Result := not IsProcessRunning('{#AppExe}') and not IsProcessRunning('CosmicCrusade-LanServer.exe')
  else
    Result := GameClosed('removing it');
end;
