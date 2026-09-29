@echo off
rem Races Explorer, PowerShell and FastDelete on THIS PC with the same
rem 10,100-file folder. Nothing on your PC is touched except the practice
rem folder (created in Temp, deleted during the race).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Benchmark-on-this-PC.ps1"
pause
