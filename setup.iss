; Requires a sign tool named "nyc" to be registered once in Inno Setup IDE:
;   Tools -> Configure Sign Tools... -> Add
;     Name:    nyc
;     Command: signtool sign /n "Mohamed Rayane Merzoug" /fd SHA256 /tr http://time.certum.pl /td SHA256 /v $f
; (or pass /Snyc="signtool sign /n ... $f" to ISCC.exe from CLI)

[Setup]
AppName=New York Chronicles
AppVersion=1.0.1
AppPublisher=New York Chronicles
DefaultDirName={autopf32}\New York Chronicles
DefaultGroupName=New York Chronicles
UninstallDisplayIcon={app}\Launcher.exe
OutputDir=Output
OutputBaseFilename=NYCSetup
SetupIconFile=NYCLauncher\Assets\icon.ico
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x86compatible
WizardStyle=modern
PrivilegesRequired=admin

SignTool=nyc
SignedUninstaller=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Dirs]
Name: "{app}"; Permissions: users-modify
Name: "{app}\game"; Permissions: users-modify
Name: "{commonappdata}\New York Chronicles"; Permissions: users-modify

[Files]
Source: "NYCLauncher\bin\Release\net48\Launcher.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "NYCLauncher\bin\Release\net48\Launcher.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "NYCLauncher\bin\Release\net48\Downloader.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "NYCLauncher\bin\Release\net48\K4os.Hash.xxHash.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "NYCLauncher\bin\Release\net48\Newtonsoft.Json.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "NYCLauncher\bin\Release\net48\System.Buffers.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "NYCLauncher\bin\Release\net48\System.Memory.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "NYCLauncher\bin\Release\net48\System.Numerics.Vectors.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "NYCLauncher\bin\Release\net48\System.Runtime.CompilerServices.Unsafe.dll"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\New York Chronicles"; Filename: "{app}\Launcher.exe"
Name: "{group}\Uninstall New York Chronicles"; Filename: "{uninstallexe}"
Name: "{commondesktop}\New York Chronicles"; Filename: "{app}\Launcher.exe"; Tasks: desktopicon

[Registry]
; Required by SharedUtil::GetMTASABaseDir() -> reads "Last Run Location" before first launch.
; Without this, fresh installs fail with Error [U01].
Root: HKLM32; Subkey: "Software\New York Chronicles";        Permissions: users-modify; Flags: uninsdeletekey
Root: HKLM32; Subkey: "Software\New York Chronicles\1.6";    Permissions: users-modify
Root: HKLM32; Subkey: "Software\New York Chronicles\Common"; Permissions: users-modify
Root: HKLM32; Subkey: "Software\New York Chronicles\1.6";    ValueType: string; ValueName: "Last Run Location";     ValueData: "{app}\game"
Root: HKLM32; Subkey: "Software\New York Chronicles\1.6";    ValueType: string; ValueName: "Last Install Location"; ValueData: "{app}\game"

Root: HKCR; Subkey: "nycl"; ValueType: string; ValueData: "URL:NYC Launcher Protocol"; Flags: uninsdeletekey
Root: HKCR; Subkey: "nycl"; ValueName: "URL Protocol"; ValueType: string; ValueData: ""
Root: HKCR; Subkey: "nycl\DefaultIcon"; ValueType: string; ValueData: """{app}\Launcher.exe"",0"
Root: HKCR; Subkey: "nycl\shell\open\command"; ValueType: string; ValueData: """{app}\Launcher.exe"" ""%1"""

[UninstallDelete]
Type: filesandordirs; Name: "{app}\game"
Type: filesandordirs; Name: "{app}"
Type: filesandordirs; Name: "{userappdata}\NYCLauncher"
Type: filesandordirs; Name: "{commonappdata}\New York Chronicles"
