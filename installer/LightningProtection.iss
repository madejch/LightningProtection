; Установщик плагина «Молниезащита для AutoCAD».
; Ставит пакет автозагрузки в %APPDATA%\Autodesk\ApplicationPlugins текущего пользователя
; (права администратора не нужны), показывает инструкцию и добавляет её в меню «Пуск».
; Сборка: build-bundle.ps1 -Installer (сначала собирается пакет dist\LightningProtection.bundle).

#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif
#define AppName "Молниезащита для AutoCAD"
#ifndef BundleDir
  #define BundleDir "..\dist\LightningProtection.bundle"
#endif

[Setup]
AppId={{525FC2B4-052A-4D52-A18A-330038E6A0DF}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=LightningProtection
DefaultDirName={userappdata}\Autodesk\ApplicationPlugins\LightningProtection.bundle
DisableDirPage=yes
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=LightningProtection-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no
InfoAfterFile=USER_GUIDE.txt
UninstallDisplayName={#AppName}
UninstallFilesDir={app}\Uninstall
; AutoCAD держит DLL плагина открытыми — Restart Manager предложит закрыть его.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Messages]
ru.WelcomeLabel2=Будет установлен плагин [name/ver] для AutoCAD 2024–2027.%n%nПлагин рассчитывает зоны защиты стержневых молниеприёмников по РД 34.21.122-87 и СО 153-34.21.122-2003 и рисует план, вид сбоку, таблицы и описательную часть.%n%nПеред установкой закройте AutoCAD.
ru.InfoAfterLabel=Как пользоваться плагином. Эта инструкция останется в меню «Пуск» → «{#AppName}».
ru.FinishedLabel=Плагин установлен. Запустите AutoCAD — на ленте появится вкладка «Молниезащита».

[InstallDelete]
; Старые версии сборок удаляются целиком, чтобы не осталось лишних файлов.
Type: filesandordirs; Name: "{app}\Contents"

[Files]
Source: "{#BundleDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
Source: "USER_GUIDE.txt"; DestDir: "{app}"; DestName: "Инструкция.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\Инструкция"; Filename: "{app}\Инструкция.txt"
Name: "{group}\Папка плагина"; Filename: "{app}"
Name: "{group}\Удалить {#AppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\Инструкция.txt"; Description: "Открыть инструкцию"; Flags: postinstall shellexec skipifsilent unchecked

[Code]
function IsAutoCADRunning: Boolean;
var
  ResultCode: Integer;
begin
#ifdef SkipAcadCheck
  // Только для проверки установщика на машине разработчика с открытым AutoCAD.
  Result := False;
  Exit;
#endif
  // tasklist с фильтром возвращает 0 в любом случае, поэтому проверяем через find.
  Result := Exec(ExpandConstant('{cmd}'), '/C tasklist /FI "IMAGENAME eq acad.exe" | find /I "acad.exe" >nul',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  while IsAutoCADRunning do
  begin
    // В тихом режиме окна подавлены и цикл «Повтор» не прервать — сразу выходим.
    if WizardSilent then
    begin
      Log('AutoCAD запущен, установка прервана.');
      Result := False;
      Exit;
    end;
    if MsgBox('AutoCAD запущен. Закройте AutoCAD, сохранив чертежи, и нажмите «Повтор».' + #13#10 +
              '«Отмена» прервёт установку.', mbError, MB_RETRYCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

function InitializeUninstall: Boolean;
begin
  Result := True;
  while IsAutoCADRunning do
  begin
    if UninstallSilent then
    begin
      Log('AutoCAD запущен, удаление прервано.');
      Result := False;
      Exit;
    end;
    if MsgBox('AutoCAD запущен. Закройте AutoCAD и нажмите «Повтор».', mbError, MB_RETRYCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
  end;
end;
