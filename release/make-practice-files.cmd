@echo off
rem Makes a practice folder with 10,000 tiny files so you can try
rem FastDelete safely. Nothing important is touched - the folder
rem is created in your Temp directory and is safe to delete.
echo.
echo Creating practice folder with 10,000 files...
set "DIR=%TEMP%\FastDeletePractice"
if exist "%DIR%" rmdir /s /q "%DIR%"
mkdir "%DIR%"
for /l %%d in (0,1,99) do (
  mkdir "%DIR%\folder%%d"
  for /l %%f in (0,1,99) do echo practice> "%DIR%\folder%%d\file%%f.txt"
)
echo.
echo Done! Now in FastDelete:
echo   1. Click "Delete a folder..." and choose:
echo        %DIR%
echo   2. Press "Select All"
echo   3. Press the red button
echo.
pause
