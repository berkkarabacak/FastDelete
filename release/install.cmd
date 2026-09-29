@echo off
setlocal
title FastDelete Setup
echo.
echo   FastDelete - Setup
echo   ------------------
echo.
echo   This installs FastDelete just for you (no admin needed):
echo     - App files go to  %LOCALAPPDATA%\Programs\FastDelete
echo     - A "FastDelete" shortcut is added to the Start Menu
echo     - "Delete with FastDelete" is added to the menu you see
echo       when you right-click a folder
echo.
set "SRC=%~dp0"
if not exist "%SRC%FastDelete.exe" (
  echo   Oops - FastDelete.exe was not found next to this script.
  echo   Keep install.cmd in the same folder as FastDelete.exe
  echo   ^(the folder you unzipped^), then double-click it again.
  echo.
  pause
  exit /b 1
)
set "DEST=%LOCALAPPDATA%\Programs\FastDelete"
if not exist "%DEST%" mkdir "%DEST%"
echo   Copying the app...
copy /y "%SRC%FastDelete.exe" "%DEST%\" >nul
if errorlevel 1 goto :copyfail
copy /y "%SRC%*.dll" "%DEST%\" >nul 2>nul
copy /y "%SRC%uninstall.cmd" "%DEST%\" >nul 2>nul
echo   Adding the Start Menu shortcut...
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -Command "$ws = New-Object -ComObject WScript.Shell; $lnk = [IO.Path]::Combine($env:APPDATA, 'Microsoft\Windows\Start Menu\Programs\FastDelete.lnk'); $s = $ws.CreateShortcut($lnk); $s.TargetPath = '%DEST%\FastDelete.exe'; $s.WorkingDirectory = '%DEST%'; $s.IconLocation = '%DEST%\FastDelete.exe'; $s.Description = 'Delete huge folders fast'; $s.Save()"
if errorlevel 1 echo   (Start Menu shortcut could not be added - the app still works.)
echo   Adding the right-click menu entry...
"%DEST%\FastDelete.exe" --install-explorer-menu
echo.
echo   All done! FastDelete is installed.
echo.
echo   Try it now: press the Windows key, type FastDelete, press Enter.
echo   Or right-click any folder and choose "Delete with FastDelete".
echo.
echo   To remove it later, run uninstall.cmd in:
echo     %DEST%
echo.
pause
exit /b 0

:copyfail
echo.
echo   Something went wrong while copying the files.
echo   Please close FastDelete if it is running, then try again.
echo.
pause
exit /b 1
