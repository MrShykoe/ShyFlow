; Скрипт установщика ShyFlow (Inno Setup 6.3 или новее).
; Собирается автоматически через build-all.bat. Версию и путь к exe подставляет build-all.ps1.

#ifndef AppVersion
  #define AppVersion "3.3.1"
#endif
#ifndef SourceExe
  #define SourceExe "out\dev\ShyFlow.exe"
#endif

[Setup]
; AppId НЕ МЕНЯТЬ: по нему Windows понимает, что это та же программа (обновление поверх, удаление).
AppId={{B7E2C1A4-5D3F-4E8A-9C61-2F0A7D4B8E15}
AppName=ShyFlow
AppVersion={#AppVersion}
AppPublisher=ShyFlow
; Ставим в папку пользователя (%LocalAppData%\Programs\ShyFlow): без прав администратора.
; Обновление: пользователь запускает новый ShyFlow-Setup.exe, он ставится поверх старой версии.
PrivilegesRequired=lowest
DefaultDirName={autopf}\ShyFlow
DefaultGroupName=ShyFlow
DisableProgramGroupPage=yes
; Имя без версии, чтобы ссылка .../releases/latest/download/ShyFlow-Setup.exe всегда работала
OutputDir=out\installer
OutputBaseFilename=ShyFlow-Setup
SetupIconFile=Island\ShyFlow.ico
UninstallDisplayIcon={app}\ShyFlow.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
russian.InstallingRuntime=Установка .NET 8 Desktop Runtime...
english.InstallingRuntime=Installing .NET 8 Desktop Runtime...
russian.RuntimeFailed=Не удалось установить .NET 8 Desktop Runtime. ShyFlow установлен, но не запустится, пока его нет. Скачай его на dotnet.microsoft.com (.NET 8, Desktop Runtime, x64).
english.RuntimeFailed=Could not install .NET 8 Desktop Runtime. ShyFlow is installed but will not start without it. Download it from dotnet.microsoft.com (.NET 8, Desktop Runtime, x64).

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\ShyFlow"; Filename: "{app}\ShyFlow.exe"
Name: "{autodesktop}\ShyFlow"; Filename: "{app}\ShyFlow.exe"; Tasks: desktopicon

[Registry]
; чистим автозапуск, который остров сам прописывает в настройках
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "ShyFlow"; ValueType: none; Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\ShyFlow.exe"; Description: "{cm:LaunchProgram,ShyFlow}"; Flags: nowait postinstall skipifsilent
; автообновление из самого ShyFlow (шаг 55): тихая установка с /relaunch=1 запускает новую версию сама
Filename: "{app}\ShyFlow.exe"; Flags: nowait; Check: ShouldRelaunch

[Code]
const
  // Microsoft сам редиректит на последний 8.0.x Desktop Runtime
  RuntimeUrl = 'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe';
  RuntimeFile = 'windowsdesktop-runtime-win-x64.exe';

var
  DownloadPage: TDownloadWizardPage;
  NeedRuntime: Boolean;

function HasDesktopRuntime8: Boolean;
var
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetValueNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if Copy(Names[I], 1, 2) = '8.' then
      begin
        Result := True;
        Exit;
      end;
end;

function ShouldRelaunch: Boolean;
begin
  Result := WizardSilent and (ExpandConstant('{param:relaunch|0}') = '1');
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  Result := True;
end;

procedure InitializeWizard;
begin
  NeedRuntime := not HasDesktopRuntime8;
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), @OnDownloadProgress);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and NeedRuntime then
  begin
    DownloadPage.Clear;
    DownloadPage.Add(RuntimeUrl, RuntimeFile, '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
      except
        if not DownloadPage.AbortedByUser then
          SuppressibleMsgBox(AddPeriod(GetExceptionMessage), mbCriticalError, MB_OK, IDOK);
        Result := False;
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if (CurStep = ssInstall) and NeedRuntime then
  begin
    WizardForm.StatusLabel.Caption := CustomMessage('InstallingRuntime');
    // runtime ставится на весь компьютер, поэтому Windows спросит разрешение (UAC)
    if (not ShellExec('runas', ExpandConstant('{tmp}\' + RuntimeFile), '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode))
       or ((ResultCode <> 0) and (ResultCode <> 3010)) then
      SuppressibleMsgBox(CustomMessage('RuntimeFailed'), mbError, MB_OK, IDOK);
  end;
end;
