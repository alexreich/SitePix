; Inno Setup script for SitePix.
; Compile with: iscc.exe /DAppVersion=1.0.0 /DPublishDir=..\..\SitePix\bin\Release\net10.0\win-x64\publish SitePix.iss
; Outputs:       packaging\inno\Output\SitePix-Setup-<version>.exe

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#ifndef PublishDir
  #define PublishDir "..\..\SitePix\bin\Release\net10.0\win-x64\publish"
#endif

#define AppName        "SitePix"
#define AppPublisher   "Alex Reich"
#define AppURL         "https://github.com/alexreich/SitePix"
#define AppExeName     "SitePix.exe"
#define TaskName       "SitePix"

[Setup]
; AppId is a stable identifier — DO NOT change across releases; it is how Inno
; detects prior installs for in-place upgrades and the Add/Remove Programs entry.
AppId={{C9B3E4D2-1A8F-4B5C-9E6D-7F8A1B2C3D4E}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\SitePix
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir=Output
OutputBaseFilename=SitePix-Setup-{#AppVersion}
Compression=lzma2/ultra
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "runonfinish"; Description: "Run SitePix now (registers the daily scheduled task and downloads the first batch of images)"; GroupDescription: "After install:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Edit configuration"; Filename: "notepad.exe"; Parameters: """{app}\appsettings.json"""
Name: "{group}\Sample profiles"; Filename: "{app}\samples"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Run now"; Flags: postinstall nowait skipifsilent; Tasks: runonfinish

[UninstallRun]
; Remove the scheduled task the app registered on first run.
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN {#TaskName} /F"; Flags: runhidden; RunOnceId: "RemoveScheduledTask"
