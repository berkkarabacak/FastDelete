#!/usr/bin/env bash
# This Git Bash environment is missing standard Windows variables; dotnet/MSBuild
# crash with "Value cannot be null (Parameter 'path1')" without them. env(1) accepts
# names with parentheses that bash's export rejects.
export PATH="$PATH:/c/Program Files/dotnet"
exec env \
  "SystemRoot=C:\\Windows" \
  "windir=C:\\Windows" \
  "ProgramData=C:\\ProgramData" \
  "ProgramFiles=C:\\Program Files" \
  "ProgramFiles(x86)=C:\\Program Files (x86)" \
  "CommonProgramFiles=C:\\Program Files\\Common Files" \
  "CommonProgramFiles(x86)=C:\\Program Files (x86)\\Common Files" \
  "PUBLIC=C:\\Users\\Public" \
  "ALLUSERSPROFILE=C:\\ProgramData" \
  "USERPROFILE=C:\\Users\\OdinLocal" \
  "APPDATA=C:\\Users\\OdinLocal\\AppData\\Roaming" \
  "LOCALAPPDATA=C:\\Users\\OdinLocal\\AppData\\Local" \
  "TEMP=C:\\Users\\OdinLocal\\AppData\\Local\\Temp" \
  "TMP=C:\\Users\\OdinLocal\\AppData\\Local\\Temp" \
  dotnet "$@"
