; LocalRack installer (Inno Setup 6).
; Build after `dotnet publish` has produced publish\LocalRack.exe:
;   ISCC.exe /DAppVersion=1.2.3 installer\LocalRack.iss
; Output: dist\LocalRack-v<version>-win-x64-setup.exe

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
; Never change AppId: Windows uses it to recognise upgrades and the uninstall entry.
AppId={{045A38D8-96F5-48E1-8915-A26FDC1F89D8}
AppName=LocalRack
AppVersion={#AppVersion}
AppVerName=LocalRack {#AppVersion}
AppPublisher=LocalRack
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\LocalRack
DefaultGroupName=LocalRack
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; Must match AppPaths.RunningMutexName: setup asks the user to exit LocalRack instead of
; replacing the exe underneath running services.
AppMutex=LocalRack-AppRunning
SetupIconFile=..\Assets\LocalRack.ico
UninstallDisplayIcon={app}\LocalRack.exe
UninstallDisplayName=LocalRack
SourceDir=.
OutputDir=..\dist
OutputBaseFilename=LocalRack-v{#AppVersion}-win-x64-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; The only per-user area touched is removing LocalRack's own Run entry on uninstall.
UsedUserAreasWarning=no

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\LocalRack.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\LocalRack"; Filename: "{app}\LocalRack.exe"
Name: "{autodesktop}\LocalRack"; Filename: "{app}\LocalRack.exe"; Tasks: desktopicon

[Registry]
; Created by the app's "Start with Windows" setting, not by setup; only cleaned up on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "LocalRack"; Flags: dontcreatekey uninsdeletevalue

[Run]
; runasoriginaluser: launch unelevated, so services started from LocalRack don't run as admin.
Filename: "{app}\LocalRack.exe"; Description: "{cm:LaunchProgram,LocalRack}"; Flags: nowait postinstall skipifsilent runasoriginaluser
