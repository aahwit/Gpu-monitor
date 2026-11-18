Unicode True
!include "MUI2.nsh"
!include "nsDialogs.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"

!define APP_NAME "GPU Monitor"
!define APP_VERSION "0.8.1"
!define COMPANY "KENRI"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\GPU Monitor"
!define SETTINGS_KEY "Software\KENRI\GPU Monitor"
!ifndef TELEMETRY_ENDPOINT
  !define TELEMETRY_ENDPOINT ""
!endif
!ifndef UPDATE_MANIFEST_URL
  !define UPDATE_MANIFEST_URL "https://github.com/aahwit/Gpu-monitor/releases/latest/download/update.json"
!endif

Var ConsentDialog
Var UpdateCheckbox
Var TelemetryCheckbox
Var AutoUpdate
Var AnonymousTelemetry

Name "${APP_NAME}"
OutFile "..\GPU-Monitor-${APP_VERSION}-Setup.exe"
InstallDir "$LOCALAPPDATA\GPU Monitor"
RequestExecutionLevel user
SetCompressor /SOLID lzma
Icon "..\src-tauri\icons\icon.ico"
UninstallIcon "..\src-tauri\icons\icon.ico"
BrandingText "GPU Monitor ${APP_VERSION} — Native Windows Widget"

!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\GPU Monitor.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Open GPU Monitor"
!insertmacro MUI_PAGE_WELCOME
Page custom ConsentPage ConsentPageLeave
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  StrCpy $AutoUpdate 1
  StrCpy $AnonymousTelemetry 1
  ReadRegDWORD $0 HKCU "${SETTINGS_KEY}" "AutoUpdate"
  IfErrors +2
  StrCpy $AutoUpdate $0
  ReadRegDWORD $0 HKCU "${SETTINGS_KEY}" "AnonymousTelemetry"
  IfErrors +2
  StrCpy $AnonymousTelemetry $0
FunctionEnd

Function ConsentPage
  nsDialogs::Create 1018
  Pop $ConsentDialog
  ${If} $ConsentDialog == error
    Abort
  ${EndIf}

  ${NSD_CreateLabel} 0 0 100% 22u "Help Improve GPU Monitor"
  Pop $0
  CreateFont $1 "Segoe UI" 13 700
  SendMessage $0 ${WM_SETFONT} $1 0

  ${NSD_CreateLabel} 0 28u 100% 20u "Choose how GPU Monitor communicates with KENRI services."
  Pop $0

  ${NSD_CreateCheckbox} 0 57u 100% 18u "Check for updates automatically"
  Pop $UpdateCheckbox
  ${If} $AutoUpdate == 1
    ${NSD_Check} $UpdateCheckbox
  ${EndIf}
  ${NSD_CreateLabel} 18u 78u 94% 18u "Receive a notification when a new version is available."
  Pop $0

  ${NSD_CreateCheckbox} 0 105u 100% 18u "Share GPU telemetry"
  Pop $TelemetryCheckbox
  ${If} $AnonymousTelemetry == 1
    ${NSD_Check} $TelemetryCheckbox
  ${EndIf}
  ${NSD_CreateLabel} 18u 126u 94% 42u "Shares GPU UUID (when available), installation ID, GPU model, app version and health metrics. The server stores a hash of the UUID to recognize the same card. No usernames, process names or file paths are sent."
  Pop $0
  ${NSD_CreateLabel} 18u 174u 94% 28u "You can change these settings later from Privacy & Updates in the system tray. Disabling anonymous usage data does not limit any features."
  Pop $0

  nsDialogs::Show
FunctionEnd

Function ConsentPageLeave
  ${NSD_GetState} $UpdateCheckbox $AutoUpdate
  ${NSD_GetState} $TelemetryCheckbox $AnonymousTelemetry
FunctionEnd

Section "Install"
  SetShellVarContext current
  nsExec::ExecToLog 'taskkill /F /IM gpu-monitor.exe'
  nsExec::ExecToLog 'taskkill /F /IM $\"GPU Monitor.exe$\"'
  SetOutPath "$INSTDIR"
  Delete "$INSTDIR\gpu-monitor.exe"
  RMDir /r "$LOCALAPPDATA\com.kenri.gpu-monitor"
  File "..\native\publish\GPU Monitor.exe"
  SetOutPath "$INSTDIR\gputaskman"
  File /r "..\src-tauri\resources\gputaskman\*.*"
  SetOutPath "$INSTDIR"
  WriteUninstaller "$INSTDIR\uninstall.exe"
  CreateDirectory "$SMPROGRAMS\GPU Monitor"
  CreateShortcut "$SMPROGRAMS\GPU Monitor\GPU Monitor.lnk" "$INSTDIR\GPU Monitor.exe"
  CreateShortcut "$DESKTOP\GPU Monitor.lnk" "$INSTDIR\GPU Monitor.exe"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "GPU Monitor"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "${COMPANY}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\GPU Monitor.exe"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '$\"$INSTDIR\uninstall.exe$\"'
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
  WriteRegDWORD HKCU "${SETTINGS_KEY}" "AutoUpdate" $AutoUpdate
  WriteRegDWORD HKCU "${SETTINGS_KEY}" "AnonymousTelemetry" $AnonymousTelemetry
  WriteRegDWORD HKCU "${SETTINGS_KEY}" "ConsentVersion" 1
  ${If} "${TELEMETRY_ENDPOINT}" != ""
    WriteRegStr HKCU "${SETTINGS_KEY}" "TelemetryEndpoint" "${TELEMETRY_ENDPOINT}"
  ${EndIf}
  ${If} "${UPDATE_MANIFEST_URL}" != ""
    WriteRegStr HKCU "${SETTINGS_KEY}" "UpdateManifestUrl" "${UPDATE_MANIFEST_URL}"
  ${EndIf}
  ${GetTime} "" "L" $0 $1 $2 $3 $4 $5 $6
  WriteRegStr HKCU "${SETTINGS_KEY}" "ConsentAcceptedAt" "$2-$1-$0 $4:$5:$6"
SectionEnd

Function .onInstSuccess
  IfSilent 0 done
  Exec '"$INSTDIR\GPU Monitor.exe"'
  done:
FunctionEnd

Section "Uninstall"
  SetShellVarContext current
  nsExec::ExecToLog 'taskkill /F /IM $\"GPU Monitor.exe$\"'
  Delete "$DESKTOP\GPU Monitor.lnk"
  Delete "$SMPROGRAMS\GPU Monitor\GPU Monitor.lnk"
  RMDir "$SMPROGRAMS\GPU Monitor"
  Delete "$INSTDIR\GPU Monitor.exe"
  Delete "$INSTDIR\gpu-monitor.exe"
  RMDir /r "$INSTDIR\gputaskman"
  RMDir /r "$LOCALAPPDATA\com.kenri.gpu-monitor"
  Delete "$INSTDIR\uninstall.exe"
  RMDir "$INSTDIR"
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  DeleteRegKey HKCU "${SETTINGS_KEY}"
SectionEnd
