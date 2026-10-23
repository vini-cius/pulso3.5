#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{A5D6CE92-2311-4FC7-9F54-2AD933B33E70}
AppName=Pulso 3.5
AppVersion={#AppVersion}
AppPublisher=Projeto Pulso 3.5
DefaultDirName={localappdata}\Programs\Pulso35
DefaultGroupName=Pulso 3.5
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
DisableProgramGroupPage=yes
CloseApplications=yes
CloseApplicationsFilter=Pulso35.exe
OutputDir=dist
OutputBaseFilename=Pulso35-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=Pulso 3.5

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos:"; Flags: unchecked

[Files]
Source: "dist\app\Pulso35.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "dist\app\Pulso35-Diagnostico.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "dist\app\versao.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "dist\app\atualizador.json"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Pulso 3.5"; Filename: "{app}\Pulso35.exe"
Name: "{autodesktop}\Pulso 3.5"; Filename: "{app}\Pulso35.exe"; Tasks: desktopicon
Name: "{group}\Diagnóstico da tela"; Filename: "{app}\Pulso35-Diagnostico.exe"

[Run]
Filename: "{app}\Pulso35.exe"; Description: "Abrir Pulso 3.5"; Flags: nowait postinstall skipifsilent
