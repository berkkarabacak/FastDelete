@echo off
setlocal
title FastDelete Uninstall
echo.
echo   FastDelete - Uninstall
echo   ----------------------
echo.
echo   This removes FastDelete from your PC:
echo     - The app folder in  %LOCALAPPDATA%\Programs\FastDelete
echo     - The Start Menu shortcut
echo     - The "Delete with FastDelete" right-click menu entry
echo.
echo   Nothing else is touched. Your files are never deleted.
echo.
set "DEST=%LOCALAPPDATA%\Programs\FastDelete"

rem 1. Remove the right-click menu entry. Ask the app nicely if it is
rem    still there; otherwise delete the registry entry ourselves.
if exist "%DEST%\FastDelete.exe" "%DEST%\FastDelete.exe" --uninstall-explorer-menu
reg delete "HKCU\Software\Classes\Directory\shell\FastDelete" /f >nul 2>nul

rem 2. Remove the Start Menu shortcut.
del /q "%APPDATA%\Microsoft\Windows\Start Menu\Programs\FastDelete.lnk" >nul 2>nul

rem 3. Remove the app folder (including this script). Step out of the
rem    folder first so Windows lets us delete it.
if exist "%DEST%" (
  cd /d "%TEMP%"
  rd /s /q "%DEST%" >nul 2>nul
)

if exist "%DEST%" (
  echo   Almost done - a few files are still locked because FastDelete
  echo   is running. Please close FastDelete and run this again.
  echo.
  pause
  exit /b 1
)

echo   All done. FastDelete has been removed.
echo.
pause
exit /b 0
