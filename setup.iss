#define MyAppName "Z-UI"
#define MyAppVersion "1.0.0"
#define MyAppExeName "Z-UI.exe"
#define SourceDir ".\publish"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=.\installer
OutputBaseFilename=Z-UI-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
MinVersion=10.0.19041
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64
SetupIconFile=.\Z-UI\Assets\Z-UI.ico
DisableDirPage=yes
DisableProgramGroupPage=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "WindowsAppRuntimeInstall-x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Удалить {#MyAppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{tmp}\WindowsAppRuntimeInstall-x64.exe"; Parameters: "--quiet --force"; StatusMsg: "Установка компонентов Windows..."; Flags: waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить {#MyAppName}"; Flags: nowait postinstall skipifsilent runascurrentuser

[Code]

const
  PBM_SETBARCOLOR = $0409;
  PBM_SETBKCOLOR  = $2001;
  THEME_REG_KEY   = 'Software\Microsoft\Windows\CurrentVersion\Themes\Personalize';
  THEME_REG_VAL   = 'AppsUseLightTheme';

  // Light palette
  L_Base      = $00F3F3F3;
  L_Surface   = $00FFFFFF;
  L_Header    = $00E8E8E8;
  L_Accent    = $00D47800;   // #0078D4 в BGR
  L_TxtPrim   = $001A1A1A;
  L_TxtSec    = $00666666;
  L_Divider   = $00E0E0E0;

  // Dark palette
  D_Base      = $00202020;
  D_Surface   = $002C2C2C;
  D_Header    = $001C1C1C;
  D_Accent    = $00D47800;
  D_TxtPrim   = $00FFFFFF;
  D_TxtSec    = $00A0A0A0;
  D_Divider   = $00383838;

var
  clBase, clSurface, clHeader,
  clAccent, clTxtPrim, clTxtSec, clDivider : Integer;
  IsDark : Boolean;

function DetectDarkMode(): Boolean;
var
  DwordVal: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKCU, THEME_REG_KEY, THEME_REG_VAL, DwordVal) then
    Result := (DwordVal = 0);
end;

procedure SelectPalette(Dark: Boolean);
begin
  if Dark then begin
    clBase    := D_Base;    clSurface := D_Surface;
    clHeader  := D_Header;  clAccent  := D_Accent;
    clTxtPrim := D_TxtPrim; clTxtSec  := D_TxtSec;
    clDivider := D_Divider;
  end else begin
    clBase    := L_Base;    clSurface := L_Surface;
    clHeader  := L_Header;  clAccent  := L_Accent;
    clTxtPrim := L_TxtPrim; clTxtSec  := L_TxtSec;
    clDivider := L_Divider;
  end;
end;

procedure SetProgressBarColor();
begin
  SendMessage(WizardForm.ProgressGauge.Handle, PBM_SETBARCOLOR, 0, clAccent);
  SendMessage(WizardForm.ProgressGauge.Handle, PBM_SETBKCOLOR,  0, clDivider);
end;

procedure ApplyTheme();
var
  F: TWizardForm;
  FontDisplay, FontText: String;
begin
  F           := WizardForm;
  FontDisplay := 'Segoe UI Variable Display';
  FontText    := 'Segoe UI Variable Text';

  F.Color := clBase;

  F.WizardBitmapImage.Visible  := False;
  F.WizardBitmapImage2.Visible := False;

  // ── TNewStaticText: PageNameLabel, PageDescriptionLabel ───
  F.PageNameLabel.Font.Name  := FontDisplay;
  F.PageNameLabel.Font.Size  := 18;
  F.PageNameLabel.Font.Style := [fsBold];
  F.PageNameLabel.Font.Color := clTxtPrim;

  F.PageDescriptionLabel.Font.Name  := FontText;
  F.PageDescriptionLabel.Font.Size  := 10;
  F.PageDescriptionLabel.Font.Style := [];
  F.PageDescriptionLabel.Font.Color := clTxtSec;

  // ── TNewStaticText: Welcome ───────────────────────────────
  F.WelcomeLabel1.Font.Name  := FontDisplay;
  F.WelcomeLabel1.Font.Size  := 22;
  F.WelcomeLabel1.Font.Style := [fsBold];
  F.WelcomeLabel1.Font.Color := clTxtPrim;

  F.WelcomeLabel2.Font.Name  := FontText;
  F.WelcomeLabel2.Font.Size  := 10;
  F.WelcomeLabel2.Font.Style := [];
  F.WelcomeLabel2.Font.Color := clTxtSec;

  // ── TNewStaticText: Finished ──────────────────────────────
  F.FinishedHeadingLabel.Font.Name  := FontDisplay;
  F.FinishedHeadingLabel.Font.Size  := 22;
  F.FinishedHeadingLabel.Font.Style := [fsBold];
  F.FinishedHeadingLabel.Font.Color := clTxtPrim;

  F.FinishedLabel.Font.Name  := FontText;
  F.FinishedLabel.Font.Size  := 10;
  F.FinishedLabel.Font.Style := [];
  F.FinishedLabel.Font.Color := clTxtSec;

  // ── Кнопки (только шрифт + размер, Color недоступен) ─────
  F.NextButton.Font.Name  := FontText;
  F.NextButton.Font.Size  := 10;
  F.NextButton.Width      := 115;
  F.NextButton.Height     := 36;

  F.BackButton.Font.Name  := FontText;
  F.BackButton.Font.Size  := 10;
  F.BackButton.Width      := 90;
  F.BackButton.Height     := 36;

  F.CancelButton.Font.Name := FontText;
  F.CancelButton.Font.Size := 10;
  F.CancelButton.Width     := 90;
  F.CancelButton.Height    := 36;

  // ── Список задач ──────────────────────────────────────────
  F.TasksList.Color      := clSurface;
  F.TasksList.Font.Name  := FontText;
  F.TasksList.Font.Size  := 10;
  F.TasksList.Font.Color := clTxtPrim;

  // ── Список компонентов ────────────────────────────────────
  F.ComponentsList.Color      := clSurface;
  F.ComponentsList.Font.Name  := FontText;
  F.ComponentsList.Font.Size  := 10;
  F.ComponentsList.Font.Color := clTxtPrim;

  // ── Лог установки ─────────────────────────────────────────
  F.FilenameLabel.Font.Name  := 'Cascadia Mono';
  F.FilenameLabel.Font.Size  := 8;
  F.FilenameLabel.Font.Color := clTxtSec;

  F.StatusLabel.Font.Name  := FontText;
  F.StatusLabel.Font.Size  := 10;
  F.StatusLabel.Font.Color := clTxtPrim;
end;

function InitializeSetup(): Boolean;
begin
  if not IsWin64 then begin
    MsgBox('Z-UI требует 64-битную Windows.', mbError, MB_OK);
    Result := False;
  end else
    Result := True;
end;

procedure InitializeWizard();
begin
  IsDark := DetectDarkMode();
  SelectPalette(IsDark);
  ApplyTheme();
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpInstalling then
    SetProgressBarColor();

  case CurPageID of
    wpWelcome:    WizardForm.NextButton.Caption := 'Установить';
    wpInstalling: WizardForm.NextButton.Caption := 'Установка...';
    wpFinished:   WizardForm.NextButton.Caption := 'Готово';
  else
    WizardForm.NextButton.Caption := 'Далее  →';
  end;
end;
