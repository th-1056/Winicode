; Compile through scripts/package.ps1. Keep AppId unchanged across every upgrade.
#ifndef AppVersion
  #define AppVersion "0.1.0-beta.1"
#endif
#ifndef PayloadDir
  #error PayloadDir must name the verified installed payload from scripts/package.ps1.
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif
#define CoreVersion Copy(AppVersion, 1, Pos("-", AppVersion + "-") - 1)
#define NumericVersion Copy(CoreVersion, 1, Pos("+", CoreVersion + "+") - 1)

[Setup]
AppId={{5B1D09D9-5EAC-4D73-BC78-4F6F9E32A971}
AppName=Winicode
AppVersion={#AppVersion}
AppPublisher=Winicode contributors
AppPublisherURL=https://github.com/th-1056/Winicode
AppSupportURL=https://github.com/th-1056/Winicode/issues
AppUpdatesURL=https://github.com/th-1056/Winicode/releases
DefaultDirName={localappdata}\Programs\Winicode
DefaultGroupName=Winicode
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.26100
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=yes
Uninstallable=yes
UninstallDisplayIcon={app}\Winicode.exe
UninstallDisplayName=Winicode
UninstallLogMode=append
SetupMutex=Local\Winicode.Setup
CloseApplications=no
RestartApplications=no
AllowNoIcons=yes
LicenseFile=..\LICENSE
InfoBeforeFile=..\THIRD-PARTY-NOTICES.md
OutputDir={#OutputDir}
OutputBaseFilename=Winicode-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile={#PayloadDir}\Assets\Winicode.ico
VersionInfoCompany=Winicode contributors
VersionInfoVersion={#NumericVersion}
VersionInfoDescription=Winicode current-user installer
VersionInfoCopyright=Copyright (c) 2026 Winicode contributors

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[CustomMessages]
english.DesktopIcon=Create a desktop shortcut
english.Startup=Start Winicode when I sign in (off by default)
english.Launch=Open Winicode
english.Uninstall=Uninstall Winicode
english.CloseFailed=Winicode could not exit safely. Exit it from its tray menu, then retry. No processes have been force-terminated.
english.StartupFailed=Winicode could not apply the startup choice. Open Settings to review it. Setup will report a nonzero exit code.
english.RemoveData=Also delete your personal settings, custom aliases, backups and Winicode logs? Choose No to retain them. This cannot be undone.
english.CleanupFailed=Winicode could not complete maintenance. Reinstall this version and retry uninstalling. Your data has been retained where cleanup has not completed.
english.Downgrade=An installed version is newer, or its version cannot be verified. Downgrades are blocked to protect your settings.
english.KeepDirectory=Upgrade in the existing Winicode directory. Moving an installation requires uninstalling it with personal data retained first.
english.PortableDirectory=This directory contains a portable Winicode copy. Choose a separate installation directory.
english.UnownedExecutable=This directory contains an unregistered Winicode executable. Choose an empty Winicode directory or repair the existing registration.
chinesesimplified.DesktopIcon=创建桌面快捷方式
chinesesimplified.Startup=登录 Windows 后启动 Winicode（默认关闭）
chinesesimplified.Launch=打开 Winicode
chinesesimplified.Uninstall=卸载 Winicode
chinesesimplified.CloseFailed=Winicode 未能安全退出。请从托盘菜单退出后重试。安装器没有强制结束进程。
chinesesimplified.StartupFailed=未能应用自启动选项。请打开设置页检查。安装器将返回非零退出码。
chinesesimplified.RemoveData=同时删除个人设置、自定义词条、备份和 Winicode 日志？选择“否”可保留这些数据。删除后无法撤销。
chinesesimplified.CleanupFailed=Winicode 未能完成维护操作。请重新安装当前版本后再卸载。尚未完成清理的数据会保留。
chinesesimplified.Downgrade=已安装版本较新，或其版本无法确认。为保护设置，安装器已阻止降级。
chinesesimplified.KeepDirectory=升级请使用现有 Winicode 目录。需要迁移时，请先卸载并保留个人数据。
chinesesimplified.PortableDirectory=该目录包含 Winicode 便携版。请选择另一个安装目录。
chinesesimplified.UnownedExecutable=该目录包含未登记的 Winicode 程序。请选择空目录，或修复原安装记录。

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked
; Upgrade never replays a stale installer startup selection over app/Windows settings.
Name: "startup"; Description: "{cm:Startup}"; Flags: unchecked; Check: IsNewInstall

[Files]
; The packaging script creates a clean, validated payload. Inno tracks each installed file.
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Winicode"; Filename: "{app}\Winicode.exe"; Parameters: "--settings"
Name: "{group}\{cm:Uninstall}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Winicode"; Filename: "{app}\Winicode.exe"; Parameters: "--settings"; Tasks: desktopicon

[Run]
Filename: "{app}\Winicode.exe"; Parameters: "--settings"; Description: "{cm:Launch}"; Flags: nowait postinstall skipifsilent unchecked

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{5B1D09D9-5EAC-4D73-BC78-4F6F9E32A971}_is1';
var
  ExistingInstall: Boolean;
  ExistingDirectory: String;
  RemovePersonalData: Boolean;
  MaintenanceFailed: Boolean;

function TakePart(var Value: String; Delimiter: String): String;
var P: Integer;
begin
  P := Pos(Delimiter, Value);
  if P = 0 then begin Result := Value; Value := ''; end
  else begin Result := Copy(Value, 1, P - 1); Delete(Value, 1, P); end;
end;

function IsNumeric(Value: String): Boolean;
var I: Integer;
begin
  Result := Length(Value) > 0;
  for I := 1 to Length(Value) do
    if (Value[I] < '0') or (Value[I] > '9') then Result := False;
end;

function CompareNumber(A, B: String): Integer;
begin
  if Length(A) < Length(B) then Result := -1
  else if Length(A) > Length(B) then Result := 1
  else Result := CompareStr(A, B);
end;

function ValidVersion(Value: String): Boolean;
var Core, Pre, Part: String; I, N, P: Integer;
begin
  Result := False;
  P := Pos('+', Value);
  if P > 0 then Value := Copy(Value, 1, P - 1);
  P := Pos('-', Value);
  Pre := '';
  Core := Value;
  if P > 0 then begin Core := Copy(Value, 1, P - 1); Pre := Copy(Value, P + 1, Length(Value)); if Pre = '' then Exit; end;
  if (Core = '') then Exit;
  if Core[Length(Core)] = '.' then Exit;
  if Pre <> '' then
    if Pre[Length(Pre)] = '.' then Exit;
  for N := 1 to 3 do begin
    Part := TakePart(Core, '.');
    if not IsNumeric(Part) then Exit;
    if (Length(Part) > 1) and (Part[1] = '0') then Exit;
    if (N < 3) and (Core = '') then Exit;
  end;
  if Core <> '' then Exit;
  while Pre <> '' do begin
    Part := TakePart(Pre, '.');
    if Part = '' then Exit;
    for I := 1 to Length(Part) do
      if not (((Part[I] >= '0') and (Part[I] <= '9')) or ((Part[I] >= 'A') and (Part[I] <= 'Z')) or ((Part[I] >= 'a') and (Part[I] <= 'z')) or (Part[I] = '-')) then Exit;
    if IsNumeric(Part) and (Length(Part) > 1) and (Part[1] = '0') then Exit;
  end;
  Result := True;
end;

function CompareReleaseVersions(A, B: String): Integer;
var ACore, BCore, APre, BPre, AP, BP: String; I, P: Integer;
begin
  P := Pos('+', A); if P > 0 then A := Copy(A, 1, P - 1);
  P := Pos('+', B); if P > 0 then B := Copy(B, 1, P - 1);
  ACore := A; BCore := B; APre := ''; BPre := '';
  P := Pos('-', A); if P > 0 then begin ACore := Copy(A, 1, P - 1); APre := Copy(A, P + 1, Length(A)); end;
  P := Pos('-', B); if P > 0 then begin BCore := Copy(B, 1, P - 1); BPre := Copy(B, P + 1, Length(B)); end;
  Result := 0;
  for I := 1 to 3 do begin
    AP := TakePart(ACore, '.'); BP := TakePart(BCore, '.');
    Result := CompareNumber(AP, BP); if Result <> 0 then Exit;
  end;
  if (APre = '') and (BPre <> '') then begin Result := 1; Exit; end;
  if (BPre = '') and (APre <> '') then begin Result := -1; Exit; end;
  while (APre <> '') or (BPre <> '') do begin
    if APre = '' then begin Result := -1; Exit; end;
    if BPre = '' then begin Result := 1; Exit; end;
    AP := TakePart(APre, '.'); BP := TakePart(BPre, '.');
    if IsNumeric(AP) and IsNumeric(BP) then Result := CompareNumber(AP, BP)
    else if IsNumeric(AP) then Result := -1
    else if IsNumeric(BP) then Result := 1
    else Result := CompareStr(AP, BP);
    if Result <> 0 then Exit;
  end;
end;

function RunMaintenance(Directory, Arguments: String): Boolean;
var Code: Integer; Executable: String;
begin
  Executable := AddBackslash(Directory) + 'Winicode.exe';
  Result := False;
  if not FileExists(Executable) then Exit;
  if Exec(Executable, Arguments, Directory, SW_HIDE, ewWaitUntilTerminated, Code) then
    Result := Code = 0;
end;

function IsNewInstall: Boolean;
begin Result := not ExistingInstall; end;

function InitializeSetup: Boolean;
var ExistingVersion: String;
begin
  Result := True;
  ExistingInstall := RegKeyExists(HKCU64, UninstallKey);
  if ExistingInstall then begin
    if not RegQueryStringValue(HKCU64, UninstallKey, 'InstallLocation', ExistingDirectory) then begin
      Result := False; Exit;
    end;
    RegQueryStringValue(HKCU64, UninstallKey, 'DisplayVersion', ExistingVersion);
    if not ValidVersion(ExistingVersion) then Result := False
    else if CompareReleaseVersions(ExistingVersion, '{#AppVersion}') > 0 then Result := False;
    if not Result then SuppressibleMsgBox(CustomMessage('Downgrade'), mbError, MB_OK, IDOK);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Directory: String;
begin
  Result := '';
  Directory := ExpandConstant('{app}');
  if FileExists(AddBackslash(Directory) + 'portable.flag') then begin Result := CustomMessage('PortableDirectory'); Exit; end;
  if ExistingInstall then begin
    if CompareText(RemoveBackslashUnlessRoot(Directory), RemoveBackslashUnlessRoot(ExistingDirectory)) <> 0 then begin Result := CustomMessage('KeepDirectory'); Exit; end;
    if not RunMaintenance(ExistingDirectory, '--exit --wait') then Result := CustomMessage('CloseFailed');
  end else if FileExists(AddBackslash(Directory) + 'Winicode.exe') then Result := CustomMessage('UnownedExecutable');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and (not ExistingInstall) then begin
    if WizardIsTaskSelected('startup') then begin
      if not RunMaintenance(ExpandConstant('{app}'), '--set-startup on') then MaintenanceFailed := True;
    end else begin
      if not RunMaintenance(ExpandConstant('{app}'), '--remove-startup') then MaintenanceFailed := True;
    end;
    if MaintenanceFailed then SuppressibleMsgBox(CustomMessage('StartupFailed'), mbError, MB_OK, IDOK);
  end;
end;

function GetCustomSetupExitCode: Integer;
begin
  Result := 0;
  if MaintenanceFailed then Result := 20;
end;

function InitializeUninstall: Boolean;
begin
  Result := True;
  RemovePersonalData := False;
  if not UninstallSilent then
    RemovePersonalData := SuppressibleMsgBox(CustomMessage('RemoveData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then begin
    if not RunMaintenance(ExpandConstant('{app}'), '--exit --wait') then RaiseException(CustomMessage('CloseFailed'));
    if not RunMaintenance(ExpandConstant('{app}'), '--remove-startup') then RaiseException(CustomMessage('CleanupFailed'));
    if RemovePersonalData then
      if not RunMaintenance(ExpandConstant('{app}'), '--cleanup-user-data') then RaiseException(CustomMessage('CleanupFailed'));
  end;
end;
// No [UninstallDelete] wildcard: only tracked installed files and empty directories are removed.
