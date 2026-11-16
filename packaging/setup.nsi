; Tonia-SoundGate 安装脚本（NSIS 3 / MUI2）
; 编译：makensis /INPUTCHARSET UTF8 setup.nsi

Unicode true
SetCompressor /SOLID lzma
SetCompressorDictSize 64

!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"

; ---------------- 基本信息 ----------------
!define APP_NAME       "Tonia-SoundGate"
!define APP_VERSION    "1.0.1"
!define APP_PUBLISHER  "Tonia"
!define APP_EXE        "Tonia-SoundGate.exe"
!define APP_KEY        "Software\Tonia-SoundGate"
!define UNINST_KEY     "Software\Microsoft\Windows\CurrentVersion\Uninstall\Tonia-SoundGate"
!define RUN_KEY        "Software\Microsoft\Windows\CurrentVersion\Run"

Name "${APP_NAME} ${APP_VERSION}"
BrandingText "${APP_NAME} ${APP_VERSION} · Tonia"
OutFile "dist\Tonia-SoundGate-Setup-${APP_VERSION}.exe"
InstallDir "$PROGRAMFILES64\${APP_NAME}"
InstallDirRegKey HKLM "${APP_KEY}" "InstallDir"
RequestExecutionLevel admin
ShowInstDetails show
ShowUninstDetails show

VIProductVersion "1.0.1.0"
VIAddVersionKey /LANG=2052 "ProductName"     "${APP_NAME}"
VIAddVersionKey /LANG=2052 "FileDescription" "${APP_NAME} 安装程序"
VIAddVersionKey /LANG=2052 "FileVersion"     "${APP_VERSION}"
VIAddVersionKey /LANG=2052 "ProductVersion"  "${APP_VERSION}"
VIAddVersionKey /LANG=2052 "CompanyName"     "${APP_PUBLISHER}"
VIAddVersionKey /LANG=2052 "LegalCopyright"  "Copyright (C) 2026 ${APP_PUBLISHER}"

; ---------------- 界面 ----------------
!define MUI_ICON   "app.ico"
!define MUI_UNICON "app.ico"
!define MUI_ABORTWARNING

!define MUI_WELCOMEPAGE_TITLE "安装 ${APP_NAME}"
!define MUI_WELCOMEPAGE_TEXT "$\r$\n${APP_NAME} 是一款外放拦截式静音守护工具：$\r$\n$\r$\n   · 常驻托盘，未授权外放时自动静音并弹窗询问$\r$\n   · 临时授权可设时长，到期自动恢复静音$\r$\n   · 避免开会解除静音后忘记恢复的尴尬$\r$\n$\r$\n程序完全在本机运行，不联网、不收集任何数据。配置保存在当前用户目录。$\r$\n$\r$\n点击「下一步」继续。"

!define MUI_DIRECTORYPAGE_TEXT_TOP "请选择 ${APP_NAME} 的安装位置。如果不需要更改，点击「下一步」继续。"

!define MUI_FINISHPAGE_TITLE "${APP_NAME} 安装完成"
!define MUI_FINISHPAGE_TEXT "程序已安装到：$\r$\n$INSTDIR$\r$\n$\r$\n启动后它会常驻系统托盘，并立即检查并强制静音。$\r$\n如果你正在外放声音，请在启动后右键托盘图标临时授权。$\r$\n$\r$\n右键托盘图标可授权外放、恢复静音、设置开机自启或退出程序。"
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "立即启动 ${APP_NAME}（会立刻静音守护）"
!define MUI_FINISHPAGE_RUN_NOTCHECKED
!define MUI_FINISHPAGE_NOREBOOTSUPPORT

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "SimpChinese"

; ---------------- 初始化：结束旧实例 ----------------
Function .onInit
    ; 旧版本正在运行会锁定 exe，先结束（失败说明本来就没在跑，忽略）
    nsExec::Exec '"$SYSDIR\taskkill.exe" /F /IM ${APP_EXE}'
    Pop $0
    Sleep 400
FunctionEnd

Function un.onInit
    nsExec::Exec '"$SYSDIR\taskkill.exe" /F /IM ${APP_EXE}'
    Pop $0
    Sleep 400

    MessageBox MB_YESNO|MB_ICONQUESTION \
        "是否同时删除个人配置（记住的授权时长、开机自启偏好）？$\r$\n$\r$\n选择「否」将保留这些设置，重新安装后仍然有效。" \
        IDYES delCfg IDNO keepCfg
    delCfg:
        Delete "$APPDATA\${APP_NAME}\settings.json"
        RMDir "$APPDATA\${APP_NAME}"
    keepCfg:
FunctionEnd

; ---------------- 安装 ----------------
Section "核心程序（必需）" SecMain
    SectionIn RO
    SetRegView 64

    SetOutPath "$INSTDIR"
    SetOverwrite try
    File "publish\${APP_EXE}"

    ; 卸载前先结束可能被占用的文件，避免残留
    WriteUninstaller "$INSTDIR\uninstall.exe"

    CreateDirectory "$SMPROGRAMS\${APP_NAME}"
    CreateShortcut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
    CreateShortcut "$SMPROGRAMS\${APP_NAME}\卸载 ${APP_NAME}.lnk" "$INSTDIR\uninstall.exe"

    WriteRegStr HKLM "${APP_KEY}" "InstallDir" "$INSTDIR"
    WriteRegStr HKLM "${APP_KEY}" "Version" "${APP_VERSION}"

    ; 控制面板「程序和功能」条目
    WriteRegStr   HKLM "${UNINST_KEY}" "DisplayName"     "${APP_NAME}"
    WriteRegStr   HKLM "${UNINST_KEY}" "DisplayVersion"  "${APP_VERSION}"
    WriteRegStr   HKLM "${UNINST_KEY}" "Publisher"       "${APP_PUBLISHER}"
    WriteRegStr   HKLM "${UNINST_KEY}" "DisplayIcon"     "$INSTDIR\${APP_EXE},0"
    WriteRegStr   HKLM "${UNINST_KEY}" "UninstallString" '"$INSTDIR\uninstall.exe"'
    WriteRegStr   HKLM "${UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\uninstall.exe" /S'
    WriteRegStr   HKLM "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
    WriteRegDWORD HKLM "${UNINST_KEY}" "NoModify" 1
    WriteRegDWORD HKLM "${UNINST_KEY}" "NoRepair" 1

    ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
    IntFmt $0 "0x%08X" $0
    WriteRegDWORD HKLM "${UNINST_KEY}" "EstimatedSize" "$0"
SectionEnd

Section "桌面快捷方式" SecDesktop
    CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
SectionEnd

; ---------------- 组件说明 ----------------
!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
    !insertmacro MUI_DESCRIPTION_TEXT ${SecMain}    "程序主体（约 64 MB，已内置 .NET 8 运行时，无需另行安装）。"
    !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} "在桌面创建启动快捷方式。托盘程序通常可从开始菜单启动，桌面图标可留可不留。"
!insertmacro MUI_FUNCTION_DESCRIPTION_END

; ---------------- 卸载 ----------------
Section "Uninstall"
    SetRegView 64
    Delete "$DESKTOP\${APP_NAME}.lnk"
    Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
    Delete "$SMPROGRAMS\${APP_NAME}\卸载 ${APP_NAME}.lnk"
    RMDir  "$SMPROGRAMS\${APP_NAME}"

    ; 程序内的「开机自启」开关会写 HKCU Run，卸载时一并清理
    DeleteRegValue HKLM "${RUN_KEY}" "${APP_NAME}"
    DeleteRegValue HKCU "${RUN_KEY}" "${APP_NAME}"

    DeleteRegKey HKLM "${UNINST_KEY}"
    DeleteRegKey HKLM "${APP_KEY}"

    Delete "$INSTDIR\${APP_EXE}"
    Delete "$INSTDIR\uninstall.exe"
    RMDir "$INSTDIR"
SectionEnd
