# Installer Build Guide (NSIS)

This project ships a Windows installer using `NSIS`.

## Prerequisites

- Windows 10/11 x64
- .NET 10 SDK
- NSIS 3.x (`makensis.exe` available in `PATH`)

## Recommended Build Command

From repository root:

```powershell
pwsh ./scripts/build-installer.ps1 -Version 1.0.0
```

Output:

- Published app: `publish/win-x64`
- Installer: `publish/DentalID-Setup-1.0.0.exe`

## Optional Parameters

```powershell
pwsh ./scripts/build-installer.ps1 `
  -Version 1.2.0 `
  -Configuration Release `
  -Runtime win-x64 `
  -SelfContained true `
  -MakensisPath "C:\Program Files (x86)\NSIS\makensis.exe"
```

## Release Checklist

1. Update application version references used by UI/About text.
2. Build installer with a clean `publish/` output.
3. Verify install on a clean VM.
4. Verify upgrade install over previous version.
5. Verify uninstall with both paths:
   - keep local data
   - remove local data
6. Verify app launch, model loading, and report generation after install.
7. (Recommended) Sign `DentalID-Setup-*.exe` before distribution.

## Notes

- NSIS script: `installer/nsis/DentalID.Setup.nsi`
- Installer now uses versioned output names by default.
- Installer checks if the app is still running before install/uninstall and prompts to close it.
