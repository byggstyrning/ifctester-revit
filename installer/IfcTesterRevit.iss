; Inno Setup Script for IfcTester Revit Plugin
; Supports Revit 2025 and 2026

#define AppName "IfcTester Revit"
#define AppVersion "1.0.0"
#define AppPublisher "Byggstyrning"
#define AppPublisherURL "https://byggstyrning.se"
#define AppId "{{3EEEF746-55D7-4E99-B04A-15A9ED3AE4F4}"
#define OutputBaseFilename "IfcTesterRevit-Setup-v1.0.0"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppPublisherURL}
AppSupportURL={#AppPublisherURL}
AppUpdatesURL={#AppPublisherURL}
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
LicenseFile=
OutputDir=..\dist
OutputBaseFilename={#OutputBaseFilename}
SetupIconFile=
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\IfcTesterRevit.dll
UninstallDisplayName={#AppName}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Installer
VersionInfoCopyright=Copyright © {#AppPublisher} 2025

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "full"; Description: "Full installation (both Revit versions)"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Components]
Name: "revit2025"; Description: "Install for Revit 2025"; Types: full; Check: IsRevit2025Installed
Name: "revit2026"; Description: "Install for Revit 2026"; Types: full; Check: IsRevit2026Installed

[Files]
; Plugin files for Revit 2025
Source: "staging\IfcTesterRevit\*"; DestDir: "{code:GetRevit2025AddinPath}\IfcTesterRevit"; Components: revit2025; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "generated\IfcTesterRevit.2025.addin"; DestDir: "{code:GetRevit2025AddinPath}"; DestName: "IfcTesterRevit.addin"; Components: revit2025; Flags: ignoreversion

; Plugin files for Revit 2026
Source: "staging\IfcTesterRevit\*"; DestDir: "{code:GetRevit2026AddinPath}\IfcTesterRevit"; Components: revit2026; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "generated\IfcTesterRevit.2026.addin"; DestDir: "{code:GetRevit2026AddinPath}"; DestName: "IfcTesterRevit.addin"; Components: revit2026; Flags: ignoreversion

[Code]
var
  Revit2025Path: string;
  Revit2026Path: string;

function GetRevitAddinPath(Year: Integer): string;
var
  AppDataPath: string;
begin
  AppDataPath := ExpandConstant('{userappdata}');
  Result := AppDataPath + '\Autodesk\Revit\Addins\' + IntToStr(Year);
  
  // Verify the path exists (Revit creates it on first run)
  if not DirExists(Result) then
  begin
    // Try to create it
    if not CreateDir(Result) then
      Result := '';
  end;
end;

function InitializeSetup(): Boolean;
begin
  Revit2025Path := GetRevitAddinPath(2025);
  Revit2026Path := GetRevitAddinPath(2026);
  
  // Check if at least one Revit version is installed
  if (Revit2025Path = '') and (Revit2026Path = '') then
  begin
    MsgBox('No supported Revit versions (2025 or 2026) were found installed on this system.' + #13#10 +
           'Please install Revit 2025 or 2026 before installing this plugin.', mbError, MB_OK);
    Result := False;
  end
  else
    Result := True;
end;

function IsRevit2025Installed(): Boolean;
begin
  Result := (Revit2025Path <> '');
end;

function IsRevit2026Installed(): Boolean;
begin
  Result := (Revit2026Path <> '');
end;

function GetRevit2025AddinPath(Param: string): string;
begin
  Result := Revit2025Path;
  if Result = '' then
    Result := GetRevitAddinPath(2025);
end;

function GetRevit2026AddinPath(Param: string): string;
begin
  Result := Revit2026Path;
  if Result = '' then
    Result := GetRevitAddinPath(2026);
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // Optionally show a message about restarting Revit
    if WizardIsComponentSelected('revit2025') or WizardIsComponentSelected('revit2026') then
    begin
      MsgBox('Installation complete!' + #13#10 +
             'Please restart Revit to load the IfcTester plugin.', mbInformation, MB_OK);
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Revit2025AddinPath: string;
  Revit2026AddinPath: string;
begin
  if CurUninstallStep = usUninstall then
  begin
    // Remove plugin files from all Revit versions
    Revit2025AddinPath := GetRevitAddinPath(2025);
    Revit2026AddinPath := GetRevitAddinPath(2026);
    
    if Revit2025AddinPath <> '' then
    begin
      if DirExists(Revit2025AddinPath + '\IfcTesterRevit') then
        DelTree(Revit2025AddinPath + '\IfcTesterRevit', True, True, True);
      if FileExists(Revit2025AddinPath + '\IfcTesterRevit.addin') then
        DeleteFile(Revit2025AddinPath + '\IfcTesterRevit.addin');
    end;
    
    if Revit2026AddinPath <> '' then
    begin
      if DirExists(Revit2026AddinPath + '\IfcTesterRevit') then
        DelTree(Revit2026AddinPath + '\IfcTesterRevit', True, True, True);
      if FileExists(Revit2026AddinPath + '\IfcTesterRevit.addin') then
        DeleteFile(Revit2026AddinPath + '\IfcTesterRevit.addin');
    end;
  end;
end;

