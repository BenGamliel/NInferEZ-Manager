#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif
#ifndef PayloadRoot
  #define PayloadRoot "..\build\payload"
#endif
#ifndef DistRoot
  #define DistRoot "..\dist"
#endif
#ifndef ProductAppId
  #define ProductAppId "{{1188B3EB-71BB-4F74-AF89-CFC3B92F227C}"
#endif
#ifndef ProductAppName
  #define ProductAppName "NInferEZ Manager"
#endif
#ifndef PackageSuffix
  #define PackageSuffix ""
#endif
#define AppName ProductAppName
#define ProjectRoot ".."

[Setup]
AppId={#ProductAppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=NInferEZ contributors
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#DistRoot}\Installer
OutputBaseFilename=NInferEZ-Manager-Setup-{#AppVersion}{#PackageSuffix}
SetupIconFile={#ProjectRoot}\assets\NInferEZ.ico
UninstallDisplayIcon={app}\NInferEZ Manager.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
LicenseFile={#PayloadRoot}\Docs\COMPONENT-TERMS.txt
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}

[Files]
Source: "{#PayloadRoot}\*"; DestDir: "{app}"; Excludes: "portable.mode"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: files; Name: "{app}\portable.mode"
Type: filesandordirs; Name: "{app}\Backend"
Type: filesandordirs; Name: "{app}\Docs"
Type: filesandordirs; Name: "{app}\Converter"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\NInferEZ Manager.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\NInferEZ Manager.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Run]
Filename: "{app}\NInferEZ Manager.exe"; Description: "Launch NInferEZ Manager"; Flags: nowait postinstall skipifsilent
Filename: "{app}\NInferEZ Manager.exe"; Flags: nowait; Check: IsAutomaticUpdate

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "{#AppName}"; Flags: uninsdeletevalue dontcreatekey

[UninstallDelete]
#ifndef QATestPackage
Type: filesandordirs; Name: "{tmp}\.net\NInferEZ Manager"
Type: filesandordirs; Name: "{tmp}\.net\NInferManager.Backend"
Type: files; Name: "{tmp}\NInferEZ-Manager-Updater-*.exe"
Type: files; Name: "{tmp}\NInferEZ-Manager-crash.txt"
Type: filesandordirs; Name: "{tmp}\NInferEZ-Manager-Diagnostics-*"
Type: filesandordirs; Name: "{tmp}\NInferEZ-Manager-Update-*"
#endif

[Code]
var
  RemoveUserData: Boolean;

function IsAutomaticUpdate(): Boolean;
begin
  Result := ExpandConstant('{param:NINFEREZUPDATE|0}') = '1';
end;

function InitializeUninstall(): Boolean;
begin
#ifdef QATestPackage
  { Isolated installer tests must never remove the real user's shared data root. }
  RemoveUserData := False;
#else
  RemoveUserData := MsgBox(
    'Remove application-managed models, downloaded engines, settings, and logs too?' + #13#10 + #13#10 +
    'Choose No to keep them. Linked model files outside the application folders are never removed.',
    mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
#endif
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
