@echo off
REM Builds TaskbarAutoHide.exe and TaskbarAutoHideSettings.exe using the
REM .NET Framework 4.8.x C# compiler included in Windows.

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not exist "%CSC%" (
    echo Could not find csc.exe. Make sure .NET Framework 4.x is enabled.
    pause
    exit /b 1
)

set EXEICON=
set WINICON=
if exist "%~dp0icon.ico" set EXEICON=/win32icon:"%~dp0icon.ico"
if exist "%~dp0icon.ico" set WINICON=/resource:"%~dp0icon.ico",AppIcon.ico

if exist "%~dp0icon.ico" (echo Found icon.ico) else (echo No icon.ico found - using default icon)
echo.

echo Building TaskbarAutoHide.exe...
"%CSC%" /nologo /target:winexe /optimize+ %EXEICON% ^
  /out:"%~dp0TaskbarAutoHide.exe" "%~dp0TaskbarAutoHide.cs"
if errorlevel 1 goto fail

echo Building TaskbarAutoHideSettings.exe...
"%CSC%" /nologo /target:winexe /optimize+ %EXEICON% %WINICON% ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll ^
  /r:System.Windows.Forms.dll /r:System.Xml.dll /r:Microsoft.CSharp.dll ^
  /out:"%~dp0TaskbarAutoHideSettings.exe" "%~dp0TaskbarAutoHideSettings.cs"
if errorlevel 1 goto fail

echo.
echo Done! Both .exe files were created next to this script.
pause
exit /b 0

:fail
echo.
echo Build failed.
pause
exit /b 1
