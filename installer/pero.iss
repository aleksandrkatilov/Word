; Установщик «Пера» (Inno Setup 6). Сборка: iscc /DAppVersion=0.2.0 /DSourceDir=..\artifacts\Pero installer\pero.iss
; Настройки (%APPDATA%\Pero) и копии восстановления (%LOCALAPPDATA%\Pero) не удаляются при обновлении
; и деинсталляции — требование «Комплекта передачи».

#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\Pero"
#endif

[Setup]
AppId={{6F1D3A52-6C2B-4E8C-9E0B-5C7A2B1F9D41}
AppName=Перо
AppVersion={#AppVersion}
AppPublisher=Aleksandr Katilov
DefaultDirName={autopf}\Pero
DefaultGroupName=Перо
UninstallDisplayIcon={app}\Pero.exe
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\artifacts
OutputBaseFilename=Pero-Setup-{#AppVersion}
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
Name: "assocdocx"; Description: "Открывать файлы .docx в «Пере»"; GroupDescription: "Ассоциации:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Перо"; Filename: "{app}\Pero.exe"
Name: "{group}\{cm:UninstallProgram,Перо}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Перо"; Filename: "{app}\Pero.exe"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\Pero.Document"; ValueType: string; ValueName: ""; ValueData: "Документ (Перо)"; Flags: uninsdeletekey; Tasks: assocdocx
Root: HKA; Subkey: "Software\Classes\Pero.Document\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\Pero.exe,0"; Tasks: assocdocx
Root: HKA; Subkey: "Software\Classes\Pero.Document\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Pero.exe"" ""%1"""; Tasks: assocdocx
Root: HKA; Subkey: "Software\Classes\.docx\OpenWithProgids"; ValueType: string; ValueName: "Pero.Document"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assocdocx
Root: HKA; Subkey: "Software\Classes\Applications\Pero.exe\SupportedTypes"; ValueType: string; ValueName: ".docx"; ValueData: ""; Flags: uninsdeletekey

[Run]
Filename: "{app}\Pero.exe"; Description: "{cm:LaunchProgram,Перо}"; Flags: nowait postinstall skipifsilent
