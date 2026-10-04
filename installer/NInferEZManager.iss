#ifndef AppVersion
  #define AppVersion "0.0.1"
#endif
#ifndef PayloadRoot
  #define PayloadRoot "..\build\payload"
#endif
#ifndef DistRoot
  #define DistRoot "..\dist"
#endif
#define AppName "NInferEZ Manager"
#define ProjectRoot ".."

[Setup]
AppId={{1188B3EB-71BB-4F74-AF89-CFC3B92F227C}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=NInferEZ contributors
DefaultDirName={localappdata}\Programs\NInferEZ Manager
DefaultGroupName=NInferEZ Manager
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#DistRoot}\Installer
OutputBaseFilename=NInferEZ-Manager-Setup-{#AppVersion}
SetupIconFile={#ProjectRoot}\assets\NInferEZ.ico
UninstallDisplayIcon={app}\NInferEZ Manager.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}

[Files]
Source: "{#PayloadRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: filesandordirs; Name: "{app}\Backend"
Type: filesandordirs; Name: "{app}\Docs"

[Icons]
Name: "{group}\NInferEZ Manager"; Filename: "{app}\NInferEZ Manager.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\NInferEZ Manager"; Filename: "{app}\NInferEZ Manager.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Run]
Filename: "{app}\NInferEZ Manager.exe"; Description: "Launch NInferEZ Manager"; Flags: nowait postinstall skipifsilent
Filename: "{app}\NInferEZ Manager.exe"; Flags: nowait; Check: IsAutomaticUpdate

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "NInferEZ Manager"; Flags: uninsdeletevalue dontcreatekey

[UninstallDelete]
Type: filesandordirs; Name: "{tmp}\.net\NInferEZ Manager"
Type: filesandordirs; Name: "{tmp}\.net\NInferManager.Backend"
Type: files; Name: "{tmp}\NInferEZ-Manager-Updater-*.exe"
Type: files; Name: "{tmp}\NInferEZ-Manager-crash.txt"
Type: filesandordirs; Name: "{tmp}\NInferEZ-Manager-Diagnostics-*"
Type: filesandordirs; Name: "{tmp}\NInferEZ-Manager-Update-*"

[Code]
var
  RemoveUserData: Boolean;

function IsAutomaticUpdate(): Boolean;
begin
  Result := ExpandConstant('{param:NINFEREZUPDATE|0}') = '1';
end;

function InitializeUninstall(): Boolean;
begin
  RemoveUserData := MsgBox(
    'Remove application-managed models, downloaded engines, settings, and logs too?' + #13#10 + #13#10 +
    'Choose No to keep them. Linked model files outside the application folders are never removed.',
    mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usUninstall) and RemoveUserData then
  begin
    DelTree(ExpandConstant('{app}\Models'), True, True, True);
    DelTree(ExpandConstant('{app}\Data'), True, True, True);
    DelTree(ExpandConstant('{localappdata}\NInferEZ Manager\Models'), True, True, True);
    DelTree(ExpandConstant('{localappdata}\NInferEZ Manager\Data'), True, True, True);
  end;
end;
