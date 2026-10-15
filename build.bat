@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
rem App name (Korean) lives in app.name so this file stays ASCII-only.
set /p APPNAME=<app.name
rem Usage: build.bat [output folder]   (default: bin)
set OUT=%~1
if "%OUT%"=="" set OUT=bin
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set CSC=%FW%\csc.exe
if not exist "%CSC%" (
  echo csc.exe not found.
  exit /b 1
)
if not exist res\app.ico (
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\make_icon.ps1 || exit /b 1
)
if not exist "%OUT%" mkdir "%OUT%"

rem If the app is running, locked files cannot be overwritten but CAN be renamed (-> .old). Then build as usual.
powershell -NoProfile -ExecutionPolicy Bypass -File tools\free_locked.ps1 "%OUT%" "%APPNAME%.exe" PdfCore.dll pdfium.dll qpdf30.dll || exit /b 1

rem Engine layer: references neither WinForms nor System.Drawing (reused as-is for a future macOS port)
"%CSC%" /nologo /codepage:65001 /utf8output /target:library /platform:x64 /optimize+ ^
  /out:"%OUT%\PdfCore.dll" /recurse:src\Core\*.cs || exit /b 1

rem UI layer (Windows WinForms)
"%CSC%" /nologo /codepage:65001 /utf8output /target:winexe /platform:x64 /optimize+ ^
  /win32icon:res\app.ico /win32manifest:res\app.manifest /resource:res\app.ico,PdfViewer.app.ico ^
  /r:"%OUT%\PdfCore.dll" /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /out:"%OUT%\%APPNAME%.exe" /recurse:src\App\*.cs || exit /b 1

copy /y third_party\pdfium\x64\pdfium.dll "%OUT%\" >nul || exit /b 1
copy /y third_party\qpdf\x64\*.dll "%OUT%\" >nul || exit /b 1
if not exist "%OUT%\licenses" mkdir "%OUT%\licenses"
if not exist "%OUT%\licenses\pdfium" mkdir "%OUT%\licenses\pdfium"
if not exist "%OUT%\licenses\qpdf" mkdir "%OUT%\licenses\qpdf"
copy /y third_party\pdfium\LICENSE "%OUT%\licenses\pdfium\LICENSE.txt" >nul
copy /y third_party\pdfium\licenses\*.* "%OUT%\licenses\pdfium\" >nul
copy /y third_party\qpdf\LICENSE.txt "%OUT%\licenses\qpdf\" >nul
copy /y third_party\qpdf\NOTICE.md "%OUT%\licenses\qpdf\" >nul
echo Build OK: %OUT%\%APPNAME%.exe
