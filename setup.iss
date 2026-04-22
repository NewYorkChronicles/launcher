; Requires a sign tool named "nyc" to be registered once in Inno Setup IDE:
;   Tools -> Configure Sign Tools... -> Add
;     Name:    nyc
;     Command: signtool sign /n "Mohamed Rayane Merzoug" /fd SHA256 /tr http://time.certum.pl /td SHA256 /v $f
; (or pass /Snyc="signtool sign /n ... $f" to ISCC.exe from CLI)

[Setup]
AppName=New York Chronicles
AppVersion=1.4.0
AppPublisher=New York Chronicles
DefaultDirName={autopf32}\New York Chronicles
DefaultGroupName=New York Chronicles
UninstallDisplayIcon={app}\Launcher.exe
OutputDir=Output
OutputBaseFilename=NYCSetup
SetupIconFile=NYCLauncher\Web\icon.ico
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x86compatible
WizardStyle=modern
PrivilegesRequired=admin

SignTool=nyc
SignedUninstaller=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
; Main executables and config
Source: "NYCLauncher\bin\Release\net48\Launcher.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "NYCLauncher\bin\Release\net48\Launcher.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "NYCLauncher\bin\Release\net48\CefSharp.BrowserSubprocess.exe"; DestDir: "{app}"; Flags: ignoreversion

; CefSharp DLLs
Source: "NYCLauncher\bin\Release\net48\CefSharp.BrowserSubprocess.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "NYCLauncher\bin\Release\net48\CefSharp.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "NYCLauncher\bin\Release\net48\CefSharp.Core.Runtime.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "NYCLauncher\bin\Release\net48\CefSharp.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "NYCLauncher\bin\Release\net48\CefSharp.Wpf.dll"; DestDir: "{app}"; Flags: ignoreversion

; Chromium / CEF resources
Source: "NYCLauncher\bin\Release\net48\chrome_100_percent.pak"; DestDir: "{app}"; Flags: ignoreversion
Source: "NYCLauncher\bin\Release\net48\chrome_200_percent.pak"; DestDir: "{app}"; Flags: ignoreversion
Source: "NYCLauncher\bin\Release\net48\chrome_elf.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "NYCLauncher\bin\Release\net48\d3dcompiler_47.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "NYCLauncher\bin\Release\net48\icudtl.dat"; DestDir: "{app}"; Flags: ignoreversion
Source: "NYCLauncher\bin\Release\net48\libcef.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "NYCLauncher\bin\Release\net48\libEGL.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "NYCLauncher\bin\Release\net48\libGLESv2.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "NYCLauncher\bin\Release\net48\resources.pak"; DestDir: "{app}"; Flags: ignoreversion
Source: "NYCLauncher\bin\Release\net48\v8_context_snapshot.bin"; DestDir: "{app}"; Flags: ignoreversion

; Vulkan / SwiftShader
Source: "NYCLauncher\bin\Release\net48\vk_swiftshader.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "NYCLauncher\bin\Release\net48\vk_swiftshader_icd.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "NYCLauncher\bin\Release\net48\vulkan-1.dll"; DestDir: "{app}"; Flags: ignoreversion

; Other dependencies
Source: "NYCLauncher\bin\Release\net48\Newtonsoft.Json.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "NYCLauncher\bin\Release\net48\Downloader.dll"; DestDir: "{app}"; Flags: ignoreversion

; Chromium locales
Source: "NYCLauncher\bin\Release\net48\locales\*.pak"; DestDir: "{app}\locales"; Flags: ignoreversion

[Icons]
Name: "{group}\New York Chronicles"; Filename: "{app}\Launcher.exe"
Name: "{group}\Uninstall New York Chronicles"; Filename: "{uninstallexe}"
Name: "{commondesktop}\New York Chronicles"; Filename: "{app}\Launcher.exe"; Tasks: desktopicon

[Registry]
Root: HKCR; Subkey: "nyc"; ValueType: string; ValueData: "URL:NYC Protocol"; Flags: uninsdeletekey
Root: HKCR; Subkey: "nyc"; ValueName: "URL Protocol"; ValueType: string; ValueData: ""
Root: HKCR; Subkey: "nyc\DefaultIcon"; ValueType: string; ValueData: """{app}\game\game.exe"",0"
Root: HKCR; Subkey: "nyc\shell\open\command"; ValueType: string; ValueData: """{app}\game\game.exe"" ""%1"""

Root: HKCR; Subkey: "nycl"; ValueType: string; ValueData: "URL:NYC Launcher Protocol"; Flags: uninsdeletekey
Root: HKCR; Subkey: "nycl"; ValueName: "URL Protocol"; ValueType: string; ValueData: ""
Root: HKCR; Subkey: "nycl\DefaultIcon"; ValueType: string; ValueData: """{app}\Launcher.exe"",0"
Root: HKCR; Subkey: "nycl\shell\open\command"; ValueType: string; ValueData: """{app}\Launcher.exe"" ""%1"""

[UninstallDelete]
Type: filesandordirs; Name: "{commonappdata}\New York Chronicles"
