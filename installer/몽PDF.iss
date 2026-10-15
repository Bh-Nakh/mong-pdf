; 몽PDF 설치 스크립트 (Inno Setup 6). release.bat 이 /DSrcDir=<최종 빌드 폴더> 로 실행한다.
#ifndef SrcDir
  #define SrcDir "..\bin_release\final"
#endif
#define AppName "몽PDF"
#define AppExe "몽PDF.exe"
#define ProgId "NoAdPDF.Document"
#define VerFull GetVersionNumbersString(SrcDir + "\" + AppExe)
#define AppVersion Copy(VerFull, 1, RPos(".", VerFull) - 1)

[Setup]
AppId={{8C4E2A5B-7F1D-4C3E-9A6B-2D5F0E1A3C47}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=NoAd
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
; 기본은 관리자 권한 없이 현재 사용자에게 설치하고, 원하면 모든 사용자 설치를 고를 수 있다.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\dist
OutputBaseFilename=몽PDF-Setup-{#AppVersion}
SetupIconFile=..\res\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ChangesAssociations=yes
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoCompany=NoAd
VersionInfoDescription={#AppName} 설치 프로그램
; 코드 서명 인증서가 생기면 SignTool 설정을 추가해 설치 파일과 프로그램에 서명한다.

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SrcDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; PDF 연결 후보로 등록한다. Windows 10/11은 기본 앱을 프로그램이 직접 바꿀 수 없어, 사용자가 설정에서 고른다.
Root: HKA; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueName: ""; ValueData: "PDF 문서"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\{#ProgId}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"
Root: HKA; Subkey: "Software\Classes\{#ProgId}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\.pdf\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".pdf"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""
; PDF 파일 우클릭 메뉴에 "몽PDF로 열기"를 등록한다 (제거 시 함께 삭제).
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.pdf\shell\NoAdPDF"; ValueType: string; ValueName: ""; ValueData: "{#AppName}(으)로 열기"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.pdf\shell\NoAdPDF"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExe}"",0"
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.pdf\shell\NoAdPDF\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKA; Subkey: "Software\NoAdPDF"; Flags: uninsdeletekeyifempty
Root: HKA; Subkey: "Software\NoAdPDF\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#AppName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\NoAdPDF\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "광고 없는 PDF 뷰어·편집기"
Root: HKA; Subkey: "Software\NoAdPDF\Capabilities"; ValueType: string; ValueName: "ApplicationIcon"; ValueData: "{app}\{#AppExe},0"
Root: HKA; Subkey: "Software\NoAdPDF\Capabilities\FileAssociations"; ValueType: string; ValueName: ".pdf"; ValueData: "{#ProgId}"
Root: HKA; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "{#AppName}"; ValueData: "Software\NoAdPDF\Capabilities"; Flags: uninsdeletevalue

[InstallDelete]
; 이름이 NoAdPDF 였던 옛 버전이 남긴 파일·바로가기를 정리한다 (같은 AppId 라 옛 설치 폴더에 그대로 덮어 설치됨).
Type: files; Name: "{app}\NoAdPDF.exe"
Type: files; Name: "{autoprograms}\NoAd PDF뷰어.lnk"
Type: files; Name: "{autodesktop}\NoAd PDF뷰어.lnk"

[Registry]
; 옛 이름(NoAdPDF.exe)으로 등록했던 '프로그램으로 열기' 항목을 정리한다.
Root: HKA; Subkey: "Software\Classes\Applications\NoAdPDF.exe"; Flags: dontcreatekey deletekey
Root: HKA; Subkey: "Software\RegisteredApplications"; ValueName: "NoAd PDF뷰어"; Flags: dontcreatekey deletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
begin
  Result := IsDotNetInstalled(net462, 0);
  if not Result then
    MsgBox('이 프로그램을 실행하려면 .NET Framework 4.6.2 이상이 필요합니다.' + #13#10 +
           'Windows 업데이트로 .NET Framework 4.8을 설치한 뒤 다시 실행해 주세요.', mbError, MB_OK);
end;
