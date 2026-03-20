; IfcTester ArchiCAD Add-On Installer
; Inno Setup Script
; 
; This installer will:
; - Install the IfcTester.apx add-on file
; - Install the web application files
; - Configure ArchiCAD to load the add-on

#define MyAppName "IfcTester for ArchiCAD"
#define MyAppVersion "1.2.5"
#define MyAppPublisher "Byggstyrning"
#define MyAppURL "https://github.com/example/ifctester"

[Setup]
AppId={{B8C9D0E1-F2A3-4B5C-6D7E-8F9A0B1C2D3E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=IfcTesterArchiCAD-Setup-win-v{#MyAppVersion}
; SetupIconFile requires .ico format - using default
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "full"; Description: "Full installation"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Components]
Name: "core"; Description: "IfcTester Add-On"; Types: full custom; Flags: fixed
Name: "ac29"; Description: "ArchiCAD 29 Support"; Types: full

[Files]
; Main add-on file (versioned builds)
Source: "..\archicad\Build\Release\IfcTesterArchiCAD.apx"; DestDir: "{app}"; Flags: ignoreversion; Components: core

; Web application files
Source: "..\web\dist\*"; DestDir: "{app}\WebApp"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: core

; Icons
Source: "..\archicad\Resources\Icons\*"; DestDir: "{app}\Resources\Icons"; Flags: ignoreversion; Components: core

; ArchiCAD 29 Add-On folder
Source: "..\archicad\Build\Release\IfcTesterArchiCAD.apx"; DestDir: "{code:GetArchiCAD29AddOnsPath}"; Flags: ignoreversion external skipifsourcedoesntexist; Components: ac29; Check: ArchiCAD29Installed

[Icons]
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Code]
var
  ArchiCAD29Path: String;

function ArchiCAD29Installed: Boolean;
begin
  Result := RegQueryStringValue(HKLM, 'SOFTWARE\GRAPHISOFT\ARCHICAD 29.0.0', 'InstallLocation', ArchiCAD29Path);
  if not Result then
    Result := RegQueryStringValue(HKCU, 'SOFTWARE\GRAPHISOFT\ARCHICAD 29.0.0', 'InstallLocation', ArchiCAD29Path);
end;

function GetArchiCAD29AddOnsPath(Param: String): String;
begin
  if ArchiCAD29Installed then
    Result := ArchiCAD29Path + '\Add-Ons'
  else
    Result := '';
end;

function InitializeSetup: Boolean;
var
  AnyArchiCADFound: Boolean;
begin
  AnyArchiCADFound := ArchiCAD29Installed;
  
  if not AnyArchiCADFound then
  begin
    if MsgBox('No supported ArchiCAD installation was found. The add-on will be installed to the application folder only.' + #13#10 + #13#10 +
              'You will need to manually copy the add-on to your ArchiCAD Add-Ons folder.' + #13#10 + #13#10 +
              'Do you want to continue?', mbConfirmation, MB_YESNO) = IDNO then
    begin
      Result := False;
      Exit;
    end;
  end;
  
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  WebAppPath: String;
begin
  if CurStep = ssPostInstall then
  begin
    // Create WebApp folder link in user's AppData for the add-on to find
    WebAppPath := ExpandConstant('{app}\WebApp');
    
    // Save web app path to registry for add-on to read
    RegWriteStringValue(HKCU, 'SOFTWARE\Byggstyrning\IfcTester', 'WebAppPath', WebAppPath);
    RegWriteStringValue(HKCU, 'SOFTWARE\Byggstyrning\IfcTester', 'Version', '{#MyAppVersion}');
  end;
end;

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[UninstallRun]
; Clean up registry entries
Filename: "reg.exe"; Parameters: "delete ""HKCU\SOFTWARE\Byggstyrning\IfcTester"" /f"; Flags: runhidden

[Registry]
Root: HKCU; Subkey: "SOFTWARE\Byggstyrning\IfcTester"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
