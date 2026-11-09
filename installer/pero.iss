; Установщик Rilvo Write (Inno Setup 6). Сборка: iscc /DAppVersion=0.3.0 /DSourceDir=..\artifacts\Pero installer\pero.iss
; Настройки (%APPDATA%\Pero), резервные версии и копии восстановления (%LOCALAPPDATA%\Pero) не удаляются
; при обновлении и деинсталляции. AppId прежний — установка обновляет ранее установленное «Перо».

#ifndef AppVersion
  #define AppVersion "0.3.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\Pero"
#endif
#define AppTitle "Rilvo Write"
#define AppExe "RilvoWrite.exe"

[Setup]
AppId={{6F1D3A52-6C2B-4E8C-9E0B-5C7A2B1F9D41}
AppName={#AppTitle}
AppVersion={#AppVersion}
AppVerName={#AppTitle} {#AppVersion}
AppPublisher=Aleksandr Katilov
VersionInfoProductName={#AppTitle}
VersionInfoDescription={#AppTitle} — установщик
DefaultDirName={autopf}\Rilvo Write
DefaultGroupName={#AppTitle}
DisableProgramGroupPage=yes
UninstallDisplayName={#AppTitle}
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=assets\rilvo.ico
WizardImageFile=assets\wizard.bmp,assets\wizard-2x.bmp
WizardSmallImageFile=assets\small.bmp,assets\small-2x.bmp
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=yes
OutputDir=..\artifacts
OutputBaseFilename=RilvoWrite-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ChangesAssociations=yes

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
; Ассоциация не включена по умолчанию, чтобы не перехватывать .docx у Word без согласия пользователя
Name: "assocdocx"; Description: "Открывать файлы .docx в Rilvo Write"; GroupDescription: "Ассоциации:"; Flags: unchecked

[InstallDelete]
; файлы прежней версии под старым именем
Type: files; Name: "{app}\Pero.exe"
Type: files; Name: "{app}\Pero.dll"
Type: files; Name: "{app}\Pero.pdb"
Type: files; Name: "{app}\Pero.deps.json"
Type: files; Name: "{app}\Pero.runtimeconfig.json"
Type: files; Name: "{group}\Перо.lnk"
Type: files; Name: "{autodesktop}\Перо.lnk"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppTitle}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppTitle}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppTitle}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\RilvoWrite.Document"; ValueType: string; ValueName: ""; ValueData: "Документ Word (Rilvo Write)"; Flags: uninsdeletekey; Tasks: assocdocx
Root: HKA; Subkey: "Software\Classes\RilvoWrite.Document\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"; Tasks: assocdocx
Root: HKA; Subkey: "Software\Classes\RilvoWrite.Document\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: assocdocx
Root: HKA; Subkey: "Software\Classes\.docx\OpenWithProgids"; ValueType: string; ValueName: "RilvoWrite.Document"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assocdocx
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppTitle}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".docx"; ValueData: ""; Flags: uninsdeletekey
; старая регистрация «Пера»
Root: HKA; Subkey: "Software\Classes\Pero.Document"; Flags: deletekey
Root: HKA; Subkey: "Software\Classes\Applications\Pero.exe"; Flags: deletekey
Root: HKA; Subkey: "Software\Classes\.docx\OpenWithProgids"; ValueType: none; ValueName: "Pero.Document"; Flags: deletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppTitle}}"; Flags: nowait postinstall skipifsilent
