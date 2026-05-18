; Inno Setup Script for IfcTester Revit Plugin
; Supports Revit 2025, 2026 and 2027

#define AppName "IfcTester Revit"
#define AppVersion "1.3.2"
#define AppPublisher "Byggstyrning"
#define AppPublisherURL "https://byggstyrning.se"
#define AppId "{{3EEEF746-55D7-4E99-B04A-15A9ED3AE4F4}"
#define OutputBaseFilename "IfcTesterRevit-Setup-v" + AppVersion

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
; Clean uninstall previous version before installing new one
CloseApplications=yes
CloseApplicationsFilter=*.dll,*.exe
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
Name: "revit2027"; Description: "Install for Revit 2027"; Types: full; Check: IsRevit2027Installed

[Files]
; Plugin files for Revit 2025 (net8.0-windows build)
Source: "staging\IfcTesterRevit\*"; DestDir: "{code:GetRevit2025AddinPath}\IfcTesterRevit"; Components: revit2025; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "generated\IfcTesterRevit.2025.addin"; DestDir: "{code:GetRevit2025AddinPath}"; DestName: "IfcTesterRevit.addin"; Components: revit2025; Flags: ignoreversion

; Plugin files for Revit 2026 (net8.0-windows build)
Source: "staging\IfcTesterRevit\*"; DestDir: "{code:GetRevit2026AddinPath}\IfcTesterRevit"; Components: revit2026; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "generated\IfcTesterRevit.2026.addin"; DestDir: "{code:GetRevit2026AddinPath}"; DestName: "IfcTesterRevit.addin"; Components: revit2026; Flags: ignoreversion

; Plugin files for Revit 2027 (net10.0-windows build)
Source: "staging\IfcTesterRevit-R27\*"; DestDir: "{code:GetRevit2027AddinPath}\IfcTesterRevit"; Components: revit2027; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "generated\IfcTesterRevit.2027.addin"; DestDir: "{code:GetRevit2027AddinPath}"; DestName: "IfcTesterRevit.addin"; Components: revit2027; Flags: ignoreversion

[Code]
var
  Revit2025Installed: Boolean;
  Revit2026Installed: Boolean;
  Revit2027Installed: Boolean;
  Revit2025AddinPath: string;
  Revit2026AddinPath: string;
  Revit2027AddinPath: string;

// Check if Revit is installed by querying the Windows Registry
// This works for both standard and custom installation locations
function IsRevitVersionInstalled(Year: Integer): Boolean;
var
  InstallPath: string;
  RegKey: string;
  YearStr: string;
begin
  Result := False;
  YearStr := IntToStr(Year);
  
  // Method 1: Check if HKLM\SOFTWARE\Autodesk\Revit\[Year] key exists
  // This is the simplest and most reliable check - if this key exists, Revit is installed
  RegKey := 'SOFTWARE\Autodesk\Revit\' + YearStr;
  if RegKeyExists(HKLM, RegKey) then
  begin
    Result := True;
    Exit;
  end;
  
  // Method 2: Check 32-bit registry view on 64-bit Windows (Wow6432Node)
  RegKey := 'SOFTWARE\Wow6432Node\Autodesk\Revit\' + YearStr;
  if RegKeyExists(HKLM, RegKey) then
  begin
    Result := True;
    Exit;
  end;
  
  // Method 3: Check for InstallationLocation in various subkey patterns
  // Some Revit editions store this differently
  RegKey := 'SOFTWARE\Autodesk\Revit\' + YearStr;
  
  // Try "Revit [Year]" subkey
  if RegQueryStringValue(HKLM, RegKey + '\Revit ' + YearStr, 'InstallationLocation', InstallPath) then
  begin
    if InstallPath <> '' then
    begin
      Result := True;
      Exit;
    end;
  end;
  
  // Try "Autodesk Revit [Year]" subkey
  if RegQueryStringValue(HKLM, RegKey + '\Autodesk Revit ' + YearStr, 'InstallationLocation', InstallPath) then
  begin
    if InstallPath <> '' then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

// Get the Addins folder path for a specific Revit version (all-users location)
// Creates the folder structure if it doesn't exist
function GetRevitAddinPath(Year: Integer): string;
var
  AppDataPath: string;
  AutodeskPath: string;
  RevitPath: string;
  AddinsPath: string;
begin
  AppDataPath := ExpandConstant('{commonappdata}');
  Result := AppDataPath + '\Autodesk\Revit\Addins\' + IntToStr(Year);
  
  // Create the full directory structure if it doesn't exist
  // This handles fresh Revit installations that haven't been run yet
  if not DirExists(Result) then
  begin
    AutodeskPath := AppDataPath + '\Autodesk';
    RevitPath := AutodeskPath + '\Revit';
    AddinsPath := RevitPath + '\Addins';
    
    // Create each level of the directory structure
    if not DirExists(AutodeskPath) then
      CreateDir(AutodeskPath);
    if not DirExists(RevitPath) then
      CreateDir(RevitPath);
    if not DirExists(AddinsPath) then
      CreateDir(AddinsPath);
    if not DirExists(Result) then
      CreateDir(Result);
  end;
end;

// Get the old per-user Addins folder path for legacy cleanup
// Does NOT create directories - read-only check for old installations
function GetRevitUserAddinPath(Year: Integer): string;
begin
  Result := ExpandConstant('{userappdata}') + '\Autodesk\Revit\Addins\' + IntToStr(Year);
end;

// Remove legacy per-user IfcTesterRevit files for a given Revit year
// Silently skips if files don't exist. Only reaches the current user's AppData.
procedure CleanupOldUserAddinFiles(Year: Integer);
var
  UserPath: string;
begin
  UserPath := GetRevitUserAddinPath(Year);
  if DirExists(UserPath + '\IfcTesterRevit') then
    DelTree(UserPath + '\IfcTesterRevit', True, True, True);
  if FileExists(UserPath + '\IfcTesterRevit.addin') then
    DeleteFile(UserPath + '\IfcTesterRevit.addin');
end;

// Uninstall previous version if found
function UninstallPreviousVersion(): Boolean;
var
  UninstallString: string;
  UninstallPath: string;
  ResultCode: Integer;
begin
  Result := True;
  
  // Check for previous installation in registry
  if RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{#AppId}_is1',
                         'UninstallString', UninstallString) then
  begin
    // Extract the path without quotes
    UninstallPath := RemoveQuotes(UninstallString);
    
    if FileExists(UninstallPath) then
    begin
      // Run the uninstaller silently
      if not Exec(UninstallPath, '/SILENT /NORESTART', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
      begin
        MsgBox('Failed to uninstall previous version. Error code: ' + IntToStr(ResultCode) + #13#10 +
               'Installation will continue anyway.', mbError, MB_OK);
      end;
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  // First, handle uninstallation of previous version
  UninstallPreviousVersion();
  
  // Detect Revit installations via registry (works with custom install locations)
  Revit2025Installed := IsRevitVersionInstalled(2025);
  Revit2026Installed := IsRevitVersionInstalled(2026);
  Revit2027Installed := IsRevitVersionInstalled(2027);

  // Check if at least one Revit version is installed
  if (not Revit2025Installed) and (not Revit2026Installed) and (not Revit2027Installed) then
  begin
    MsgBox('No supported Revit versions (2025, 2026 or 2027) were found installed on this system.' + #13#10 + #13#10 +
           'The installer checks the Windows Registry for Revit installations.' + #13#10 +
           'Please install Revit 2025, 2026 or 2027 before installing this plugin.', mbError, MB_OK);
    Result := False;
  end
  else
  begin
    // Pre-compute addin paths for detected versions
    if Revit2025Installed then
      Revit2025AddinPath := GetRevitAddinPath(2025);
    if Revit2026Installed then
      Revit2026AddinPath := GetRevitAddinPath(2026);
    if Revit2027Installed then
      Revit2027AddinPath := GetRevitAddinPath(2027);
    Result := True;
  end;
end;

function IsRevit2025Installed(): Boolean;
begin
  Result := Revit2025Installed;
end;

function IsRevit2026Installed(): Boolean;
begin
  Result := Revit2026Installed;
end;

function IsRevit2027Installed(): Boolean;
begin
  Result := Revit2027Installed;
end;

function GetRevit2025AddinPath(Param: string): string;
begin
  if Revit2025AddinPath <> '' then
    Result := Revit2025AddinPath
  else
    Result := GetRevitAddinPath(2025);
end;

function GetRevit2026AddinPath(Param: string): string;
begin
  if Revit2026AddinPath <> '' then
    Result := Revit2026AddinPath
  else
    Result := GetRevitAddinPath(2026);
end;

function GetRevit2027AddinPath(Param: string): string;
begin
  if Revit2027AddinPath <> '' then
    Result := Revit2027AddinPath
  else
    Result := GetRevitAddinPath(2027);
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // Clean up old per-user addin files left by previous installer versions
    // This only reaches the current user's AppData (other users' files are unreachable)
    if WizardIsComponentSelected('revit2025') then
      CleanupOldUserAddinFiles(2025);
    if WizardIsComponentSelected('revit2026') then
      CleanupOldUserAddinFiles(2026);
    if WizardIsComponentSelected('revit2027') then
      CleanupOldUserAddinFiles(2027);

    // Show a message about restarting Revit
    if WizardIsComponentSelected('revit2025') or WizardIsComponentSelected('revit2026') or WizardIsComponentSelected('revit2027') then
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
  Revit2027AddinPath: string;
begin
  if CurUninstallStep = usUninstall then
  begin
    // Remove plugin files from the all-users ProgramData location
    Revit2025AddinPath := GetRevitAddinPath(2025);
    Revit2026AddinPath := GetRevitAddinPath(2026);
    Revit2027AddinPath := GetRevitAddinPath(2027);

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

    if Revit2027AddinPath <> '' then
    begin
      if DirExists(Revit2027AddinPath + '\IfcTesterRevit') then
        DelTree(Revit2027AddinPath + '\IfcTesterRevit', True, True, True);
      if FileExists(Revit2027AddinPath + '\IfcTesterRevit.addin') then
        DeleteFile(Revit2027AddinPath + '\IfcTesterRevit.addin');
    end;

    // Also clean up legacy per-user files from old installer versions
    // Only reaches the current user's AppData
    CleanupOldUserAddinFiles(2025);
    CleanupOldUserAddinFiles(2026);
    CleanupOldUserAddinFiles(2027);
  end;
end;

