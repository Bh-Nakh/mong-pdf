@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
set /p APPNAME=<app.name
rem Release build: plain build -> obfuscate (ConfuserEx2) -> installer (Inno Setup) -> dist\
set REL=bin_release

if exist "%REL%" rmdir /s /q "%REL%"
call "%~dp0build.bat" "%REL%\plain" || exit /b 1

if not exist tools\confuser\Confuser.CLI.exe (
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\get_tools.ps1 || exit /b 1
)
copy /y tools\confuser.crproj "%REL%\plain\" >nul
tools\confuser\Confuser.CLI.exe -n "%REL%\plain\confuser.crproj" || exit /b 1

xcopy /e /i /q "%REL%\plain" "%REL%\final" >nul
del "%REL%\final\confuser.crproj"
rmdir /s /q "%REL%\final\obf"
copy /y "%REL%\plain\obf\%APPNAME%.exe" "%REL%\final\" >nul || exit /b 1
copy /y "%REL%\plain\obf\PdfCore.dll" "%REL%\final\" >nul || exit /b 1

set ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" set ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" (
  echo Inno Setup 6 not found. Install: winget install JRSoftware.InnoSetup
  exit /b 1
)
"%ISCC%" /Q "/DSrcDir=%CD%\%REL%\final" installer\%APPNAME%.iss || exit /b 1
echo Release OK: see dist\
