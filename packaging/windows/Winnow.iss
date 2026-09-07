; This source is compiled by New-WindowsPackage.ps1. The /D symbols keep the
; installer independent of CI's publish and artifact directories.

[Setup]
AppId={{A2A9E417-5D4B-4B85-8738-7D6E993E51CE}
AppName=Winnow
AppVersion={#AppVersion}
AppVerName=Winnow {#AppVersion}
DefaultDirName={localappdata}\Programs\Winnow
UsePreviousAppDir=yes
DefaultGroupName=Winnow
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=Winnow-{#AppVersion}-win-x64-setup
SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\Winnow.exe
VersionInfoVersion={#NumericVersion}
VersionInfoTextVersion={#AppVersion}
Compression=lzma2
SolidCompression=yes

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Winnow"; Filename: "{app}\Winnow.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Winnow.exe"

; Winnow's database, cache, themes, and browser profile live under
; %LOCALAPPDATA%\Winnow. They are not installer files, so Inno Setup does not
; remove them on upgrade or uninstall.
