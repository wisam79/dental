Unicode true

!include "MUI2.nsh"
!include "x64.nsh"

!define APP_NAME "DentalID"
!ifndef APP_VERSION
!define APP_VERSION "1.0.0"
!endif
!ifndef APP_VERSION_FILE
!define APP_VERSION_FILE "1.0.0.0"
!endif
!define APP_PUBLISH_DIR "..\..\publish\win-x64"
!define OUTPUT_FILE "..\..\publish\DentalID-Setup.exe"
!define UNINSTALL_REG_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}"
!define APP_EXE "DentalID.Desktop.exe"

!if /FileExists "${APP_PUBLISH_DIR}\${APP_EXE}"
!else
!error "Publish output not found: ${APP_PUBLISH_DIR}\${APP_EXE}. Run dotnet publish first."
!endif

Var RemoveUserData
Var DataBackupPath

Name "${APP_NAME}"
OutFile "${OUTPUT_FILE}"
InstallDir "$LocalAppData\${APP_NAME}"
InstallDirRegKey HKCU "${UNINSTALL_REG_KEY}" "InstallLocation"

RequestExecutionLevel user
SetCompressor /SOLID lzma
SetOverwrite on
BrandingText "DentalID Forensic Installer"

VIProductVersion "${APP_VERSION_FILE}"
VIAddVersionKey "ProductName" "${APP_NAME}"
VIAddVersionKey "FileDescription" "${APP_NAME} Installer"
VIAddVersionKey "ProductVersion" "${APP_VERSION}"
VIAddVersionKey "FileVersion" "${APP_VERSION}"
VIAddVersionKey "CompanyName" "DentalID"
VIAddVersionKey "LegalCopyright" "DentalID"

!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

!insertmacro MUI_LANGUAGE "English"

Function .onInit
    ${IfNot} ${RunningX64}
        MessageBox MB_ICONSTOP|MB_OK "This installer supports 64-bit Windows only."
        Abort
    ${EndIf}
FunctionEnd

Function un.onInit
    StrCpy $RemoveUserData "0"

    IfSilent done
    IfFileExists "$INSTDIR\data\*.*" prompt done
prompt:
    MessageBox MB_ICONQUESTION|MB_YESNO|MB_DEFBUTTON2 "Do you want to remove local data (database, settings, keys) from '$INSTDIR\data'?" IDYES setremove IDNO done
setremove:
    StrCpy $RemoveUserData "1"
done:
FunctionEnd

Section "Install" SecInstall
    SetShellVarContext current
    SetOutPath "$INSTDIR"

    IfFileExists "$INSTDIR\Uninstall.exe" 0 +3
    DetailPrint "Existing installation detected. Replacing in-place."

    File /r "${APP_PUBLISH_DIR}\*"

    WriteUninstaller "$INSTDIR\Uninstall.exe"

    CreateDirectory "$SMPROGRAMS\${APP_NAME}"
    CreateShortcut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"
    CreateShortcut "$SMPROGRAMS\${APP_NAME}\Uninstall ${APP_NAME}.lnk" "$INSTDIR\Uninstall.exe"
    CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"

    WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "DisplayName" "${APP_NAME}"
    WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "DisplayVersion" "${APP_VERSION}"
    WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "Publisher" "DentalID"
    WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
    WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
    WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
    WriteRegDWORD HKCU "${UNINSTALL_REG_KEY}" "NoModify" 1
    WriteRegDWORD HKCU "${UNINSTALL_REG_KEY}" "NoRepair" 1
SectionEnd

Section "Uninstall"
    SetShellVarContext current

    StrCpy $DataBackupPath "$TEMP\${APP_NAME}_data_backup"
    RMDir /r "$DataBackupPath"

    ${If} $RemoveUserData != "1"
        IfFileExists "$INSTDIR\data\*.*" 0 +2
        Rename "$INSTDIR\data" "$DataBackupPath"
    ${EndIf}

    Delete "$DESKTOP\${APP_NAME}.lnk"
    Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
    Delete "$SMPROGRAMS\${APP_NAME}\Uninstall ${APP_NAME}.lnk"
    RMDir "$SMPROGRAMS\${APP_NAME}"

    Delete "$INSTDIR\Uninstall.exe"
    RMDir /r "$INSTDIR"

    ${If} $RemoveUserData != "1"
        IfFileExists "$DataBackupPath\*.*" 0 +4
        CreateDirectory "$INSTDIR"
        Rename "$DataBackupPath" "$INSTDIR\data"
    ${EndIf}

    DeleteRegKey HKCU "${UNINSTALL_REG_KEY}"
SectionEnd
