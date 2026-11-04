[Setup]
AppId=ArgueBot
AppName=ArgueBot
AppVersion={#Version}
DefaultDirName={localappdata}\Programs\ArgueBot
DefaultGroupName=ArgueBot
PrivilegesRequired=lowest
ArchitecturesAllowed={#Architecture}
OutputDir={#Output}
OutputBaseFilename={#Artifact}
Compression=lzma2
SolidCompression=yes
InfoBeforeFile={#Source}\UNSIGNED.txt
UninstallDisplayIcon={app}\ArgueBot.Cli.exe

[Files]
Source: "{#Source}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ArgueBot web"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\arguebot-web.ps1"""; Flags: runminimized
Name: "{group}\ArgueBot terminal"; Filename: "{app}\ArgueBot.Tui.exe"
Name: "{group}\Uninstall ArgueBot"; Filename: "{uninstallexe}"

[Tasks]
Name: startup; Description: "Start the ArgueBot workspace when I sign in"; Flags: unchecked

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: ArgueBot; ValueData: """{app}\ArgueBot.Server.exe"" --detached"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\ArgueBot.Tui.exe"; Description: "Open ArgueBot terminal"; Flags: postinstall nowait skipifsilent
