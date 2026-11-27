# Audio Taskbar — per-user NSIS installer (no admin / no UAC).
# Version comes from -DAPP_VERSION=x.y.z (scripts/build-installer.ps1 reads src/AudioTaskbar.csproj).
# Build:  makensis -INPUTCHARSET UTF8 installer\AudioTaskbar.nsi
#      or scripts\build-installer.ps1
#
# Silent flags (for automated tests; /D= must be last and unquoted):
#   /S            silent install
#   /NORUN        do not launch the app when finished
#   /NODESKTOP    do not create a Desktop shortcut
#   /NOSTARTMENU  do not create a Start Menu shortcut
#   /AUTOSTART    add HKCU ...\Run autostart (off by default; kept on upgrade if already set)
#   /UPDATED      (auto-updater) relaunch with --minimized --updated after a silent upgrade
#   /D=path       install directory
# Uninstall:  Uninstall.exe /S          (keeps %LOCALAPPDATA%\AudioTaskbar)
#             Uninstall.exe /S /PURGE   (also removes user config/logs)

Unicode true
ManifestDPIAware true
ManifestLongPathAware true
SetCompressor /SOLID lzma
SetCompressorDictSize 64
SetDatablockOptimize on
CRCCheck on
ShowInstDetails show
ShowUninstDetails show
AllowRootDirInstall false
RequestExecutionLevel user

!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "WinVer.nsh"
!include "Sections.nsh"

!define APP_NAME           "Audio Taskbar"
!define APP_NAME_SHORT     "AudioTaskbar"
!ifndef APP_VERSION
  !define APP_VERSION      "5.3.1"
!endif
!define APP_VERSION_FULL   "${APP_VERSION}.0"
!define APP_PUBLISHER      "Audio Taskbar"
!define APP_EXE            "AudioTaskbar.exe"
!define APP_ICO            "AudioTaskbar.ico"
!define UNINST_KEY         "Software\Microsoft\Windows\CurrentVersion\Uninstall\AudioTaskbar"
!define RUN_KEY            "Software\Microsoft\Windows\CurrentVersion\Run"
!define RUN_VALUE          "AudioTaskbar"

!ifndef PAYLOAD_EXE
  !define PAYLOAD_EXE "..\release\AudioTaskbar.exe"
!endif
!ifndef OUTFILE
  !define OUTFILE "..\installer-output\AudioTaskbar-Setup-${APP_VERSION}.exe"
!endif

!if /FileExists "${PAYLOAD_EXE}"
!else
  !error "Missing ${PAYLOAD_EXE}. Build the app with scripts\build.ps1 first, then re-run the installer build."
!endif

Name        "${APP_NAME}"
OutFile     "${OUTFILE}"
InstallDir  "$LOCALAPPDATA\Programs\AudioTaskbar"
InstallDirRegKey HKCU "${UNINST_KEY}" "InstallLocation"
BrandingText "${APP_NAME} ${APP_VERSION}"

VIProductVersion "${APP_VERSION_FULL}"
VIAddVersionKey "ProductName"        "${APP_NAME}"
VIAddVersionKey "ProductVersion"     "${APP_VERSION}"
VIAddVersionKey "FileVersion"        "${APP_VERSION_FULL}"
VIAddVersionKey "FileDescription"    "${APP_NAME} Setup"
VIAddVersionKey "CompanyName"        "${APP_PUBLISHER}"
VIAddVersionKey "LegalCopyright"     "${APP_PUBLISHER}"
VIAddVersionKey "OriginalFilename"   "AudioTaskbar-Setup-${APP_VERSION}.exe"

!define MUI_ICON                      "assets\audiotaskbar.ico"
!define MUI_UNICON                    "assets\audiotaskbar.ico"
!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_BITMAP        "assets\header.bmp"
!define MUI_HEADERIMAGE_UNBITMAP      "assets\header.bmp"
!define MUI_WELCOMEFINISHPAGE_BITMAP  "assets\welcome.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "assets\welcome.bmp"
!define MUI_ABORTWARNING
!define MUI_UNABORTWARNING
!define MUI_LANGDLL_REGISTRY_ROOT     HKCU
!define MUI_LANGDLL_REGISTRY_KEY      "Software\${APP_NAME_SHORT}"
!define MUI_LANGDLL_REGISTRY_VALUENAME "InstallerLanguage"

!define MUI_WELCOMEPAGE_TITLE         "$(WELCOME_TITLE)"
!define MUI_WELCOMEPAGE_TEXT          "$(WELCOME_TEXT)"
!define MUI_FINISHPAGE_RUN            "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT       "$(FINISH_RUN)"
!define MUI_FINISHPAGE_NOREBOOTSUPPORT
!define MUI_FINISHPAGE_NOAUTOCLOSE
!define MUI_UNFINISHPAGE_NOAUTOCLOSE

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_WELCOME
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

# Vietnamese first so it is the fallback; the language dialog follows the OS UI.
!insertmacro MUI_LANGUAGE "Vietnamese"
!insertmacro MUI_LANGUAGE "English"

!insertmacro GetParameters
!insertmacro GetOptions
!insertmacro GetSize
!insertmacro un.GetParameters
!insertmacro un.GetOptions

LangString WELCOME_TITLE ${LANG_VIETNAMESE} "Cài đặt ${APP_NAME} ${APP_VERSION}"
LangString WELCOME_TITLE ${LANG_ENGLISH}    "Install ${APP_NAME} ${APP_VERSION}"
LangString WELCOME_TEXT  ${LANG_VIETNAMESE} "Trình cài sẽ cài ${APP_NAME} — diorama thời tiết sống trên thanh taskbar Windows 11.$\r$\n$\r$\nKhông cần quyền Administrator hay .NET SDK. Sau khi cài, nhấn F8 để bật/tắt hiệu ứng.$\r$\n$\r$\nBấm Tiếp để tiếp tục."
LangString WELCOME_TEXT  ${LANG_ENGLISH}    "This will install ${APP_NAME} — a living weather diorama on the Windows 11 taskbar.$\r$\n$\r$\nNo Administrator rights or .NET SDK are required. After setup, press F8 to toggle the effect.$\r$\n$\r$\nClick Next to continue."
LangString FINISH_RUN    ${LANG_VIETNAMESE} "Chạy ${APP_NAME} ngay sau khi cài xong"
LangString FINISH_RUN    ${LANG_ENGLISH}    "Run ${APP_NAME} when Setup finishes"
LangString SEC_CORE      ${LANG_VIETNAMESE} "${APP_NAME} (bắt buộc)"
LangString SEC_CORE      ${LANG_ENGLISH}    "${APP_NAME} (required)"
LangString SEC_DESKTOP   ${LANG_VIETNAMESE} "Tạo shortcut trên Desktop"
LangString SEC_DESKTOP   ${LANG_ENGLISH}    "Create a Desktop shortcut"
LangString SEC_STARTMENU ${LANG_VIETNAMESE} "Tạo shortcut trên Start Menu"
LangString SEC_STARTMENU ${LANG_ENGLISH}    "Create a Start Menu shortcut"
LangString SEC_AUTOSTART ${LANG_VIETNAMESE} "Tự khởi động cùng Windows"
LangString SEC_AUTOSTART ${LANG_ENGLISH}    "Start ${APP_NAME} with Windows"
LangString DESC_CORE     ${LANG_VIETNAMESE} "Ứng dụng ${APP_NAME} (self-contained, không cần .NET SDK) cùng README và file cấu hình mẫu. Cài vào thư mục người dùng, không cần quyền admin."
LangString DESC_CORE     ${LANG_ENGLISH}    "The ${APP_NAME} app (self-contained, no .NET SDK) plus the README and a sample config. Installed per-user, no admin rights required."
LangString DESC_DESKTOP  ${LANG_VIETNAMESE} "Đặt lối tắt ${APP_NAME} trên Desktop."
LangString DESC_DESKTOP  ${LANG_ENGLISH}    "Place an ${APP_NAME} shortcut on the Desktop."
LangString DESC_STARTMENU ${LANG_VIETNAMESE} "Đặt lối tắt ${APP_NAME} trên Start Menu."
LangString DESC_STARTMENU ${LANG_ENGLISH}    "Place an ${APP_NAME} shortcut on the Start Menu."
LangString DESC_AUTOSTART ${LANG_VIETNAMESE} "Đăng ký tự chạy cùng Windows (HKCU Run). Mặc định tắt — chỉ bật khi bạn muốn."
LangString DESC_AUTOSTART ${LANG_ENGLISH}    "Register a per-user startup entry (HKCU Run). Off by default — enable only if you want it."
LangString UN_PURGE      ${LANG_VIETNAMESE} "Bạn có muốn xóa cả cấu hình và nhật ký cá nhân không?$\r$\n$\r$\nChọn No để giữ lại cài đặt (khuyến nghị)."
LangString UN_PURGE      ${LANG_ENGLISH}    "Do you also want to remove your personal settings and logs?$\r$\n$\r$\nChoose No to keep them (recommended)."
LangString UN_NEED_CLOSE ${LANG_VIETNAMESE} "Không gỡ được file đang chạy. Hãy thoát ${APP_NAME} (icon khay hoặc F8 rồi Thoát) rồi thử lại. Không được End Task đối với Explorer."
LangString UN_NEED_CLOSE ${LANG_ENGLISH}    "A running file could not be removed. Please quit ${APP_NAME} (tray icon → Exit) and try again. Do not kill Explorer."
LangString ERR_ARCH      ${LANG_VIETNAMESE} "${APP_NAME} chỉ hỗ trợ Windows 64-bit (x64)."
LangString ERR_ARCH      ${LANG_ENGLISH}    "${APP_NAME} requires 64-bit (x64) Windows."

Var PurgeData

!macro StopInstalled UN
Function ${UN}StopInstalledCopy
  InitPluginsDir
  File /oname=$PLUGINSDIR\stop-installed.ps1 "stop-installed.ps1"
  nsExec::ExecToLog '"$WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "$PLUGINSDIR\stop-installed.ps1" -InstallDir "$INSTDIR"'
  Pop $0
FunctionEnd
!macroend
!insertmacro StopInstalled ""
!insertmacro StopInstalled "un."

Section "$(SEC_CORE)" SecCore
  SectionIn RO
  Call StopInstalledCopy

  SetOutPath $INSTDIR
  # Payload only — never ship a personal config.json (tokyo / hidden HUD, etc.).
  # New users get clean in-app defaults: regionMode=auto, showWeatherHud=true,
  # trainCrossingMinutes=1, timeMode=cycle, dayCycleMinutes=30, tapMode=auto.
  File "/oname=${APP_EXE}" "${PAYLOAD_EXE}"
  File "/oname=${APP_ICO}" "assets\audiotaskbar.ico"
  File "..\README.md"
  File "..\NOTICE.md"
  File "..\config.example.json"

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  WriteRegStr HKCU "${UNINST_KEY}" "DisplayName"     "${APP_NAME}"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayVersion"  "${APP_VERSION}"
  WriteRegStr HKCU "${UNINST_KEY}" "Publisher"       "${APP_PUBLISHER}"
  WriteRegStr HKCU "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayIcon"     "$INSTDIR\${APP_EXE},0"
  WriteRegStr HKCU "${UNINST_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoRepair" 1
  WriteRegStr HKCU "${UNINST_KEY}" "URLInfoAbout" ""
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  WriteRegDWORD HKCU "${UNINST_KEY}" "EstimatedSize" $0
SectionEnd

Section "$(SEC_DESKTOP)" SecDesktop
  SetOutPath $INSTDIR
  CreateShortCut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_ICO}" 0 SW_SHOWNORMAL "" "${APP_NAME}"
SectionEnd

Section "$(SEC_STARTMENU)" SecStartMenu
  SetOutPath $INSTDIR
  CreateShortCut "$SMPROGRAMS\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_ICO}" 0 SW_SHOWNORMAL "" "${APP_NAME}"
SectionEnd

Section /o "$(SEC_AUTOSTART)" SecAutostart
  WriteRegStr HKCU "${RUN_KEY}" "${RUN_VALUE}" '"$INSTDIR\${APP_EXE}" --minimized'
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecCore}      $(DESC_CORE)
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop}   $(DESC_DESKTOP)
  !insertmacro MUI_DESCRIPTION_TEXT ${SecStartMenu} $(DESC_STARTMENU)
  !insertmacro MUI_DESCRIPTION_TEXT ${SecAutostart} $(DESC_AUTOSTART)
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Section "Uninstall"
  Call un.StopInstalledCopy

  Delete "$INSTDIR\${APP_EXE}"
  IfFileExists "$INSTDIR\${APP_EXE}" 0 +3
    MessageBox MB_OK|MB_ICONEXCLAMATION "$(UN_NEED_CLOSE)"
    Abort

  Delete "$INSTDIR\${APP_ICO}"
  Delete "$INSTDIR\README.md"
  Delete "$INSTDIR\NOTICE.md"
  Delete "$INSTDIR\config.example.json"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir  "$INSTDIR"

  Delete "$DESKTOP\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}.lnk"
  DeleteRegValue HKCU "${RUN_KEY}" "${RUN_VALUE}"
  DeleteRegKey HKCU "${UNINST_KEY}"
  DeleteRegKey HKCU "Software\${APP_NAME_SHORT}"

  IfSilent silent_purge ask_purge
  ask_purge:
    MessageBox MB_YESNO|MB_ICONQUESTION|MB_DEFBUTTON2 "$(UN_PURGE)$\r$\n$\r$\n$LOCALAPPDATA\AudioTaskbar" IDYES do_purge
    Goto skip_purge
  silent_purge:
    ${If} $PurgeData == 1
      Goto do_purge
    ${EndIf}
    Goto skip_purge
  do_purge:
    RMDir /r "$LOCALAPPDATA\AudioTaskbar"
  skip_purge:
SectionEnd

Function .onInit
  ${If} ${RunningX64}
    SetRegView 64
  ${Else}
    MessageBox MB_OK|MB_ICONSTOP "$(ERR_ARCH)"
    Abort
  ${EndIf}

  !insertmacro MUI_LANGDLL_DISPLAY

  ${GetParameters} $R0
  ClearErrors
  ${GetOptions} $R0 "/NODESKTOP" $R1
  ${IfNot} ${Errors}
    !insertmacro UnselectSection ${SecDesktop}
  ${EndIf}
  ClearErrors
  ${GetOptions} $R0 "/NOSTARTMENU" $R1
  ${IfNot} ${Errors}
    !insertmacro UnselectSection ${SecStartMenu}
  ${EndIf}
  ClearErrors
  ${GetOptions} $R0 "/AUTOSTART" $R1
  ${IfNot} ${Errors}
    !insertmacro SelectSection ${SecAutostart}
  ${EndIf}
  # Upgrade: keep an existing autostart entry (rewritten with --minimized).
  ClearErrors
  ReadRegStr $R2 HKCU "${RUN_KEY}" "${RUN_VALUE}"
  ${IfNot} ${Errors}
  ${AndIf} $R2 != ""
    !insertmacro SelectSection ${SecAutostart}
  ${EndIf}
FunctionEnd

Function un.onInit
  !insertmacro MUI_UNGETLANGUAGE
  ${If} ${RunningX64}
    SetRegView 64
  ${EndIf}
  StrCpy $PurgeData 0
  ${un.GetParameters} $R0
  ClearErrors
  ${un.GetOptions} $R0 "/PURGE" $R1
  ${IfNot} ${Errors}
    StrCpy $PurgeData 1
  ${EndIf}
FunctionEnd

Function .onInstSuccess
  # Interactive installs use the Finish-page checkbox (checked by default).
  # Silent installs launch the app unless /NORUN is passed.
  IfSilent +1 skip_silent_run
  ${GetParameters} $R0
  ClearErrors
  ${GetOptions} $R0 "/NORUN" $R1
  ${If} ${Errors}
    SetOutPath $INSTDIR
    ClearErrors
    ${GetOptions} $R0 "/UPDATED" $R1
    ${If} ${Errors}
      Exec '"$INSTDIR\${APP_EXE}" --minimized'
    ${Else}
      Exec '"$INSTDIR\${APP_EXE}" --minimized --updated'
    ${EndIf}
  ${EndIf}
  skip_silent_run:
FunctionEnd


