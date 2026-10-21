@echo off
rem Build AgoAnchor with the .NET Framework compiler bundled with Windows.
rem No .NET SDK / MSBuild / Visual Studio required.
setlocal
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo [ERROR] C# compiler not found at:
    echo         %CSC%
    echo .NET Framework 4.x is required ^(included with Windows 10/11^).
    exit /b 1
)

if not exist build mkdir build

set REFS=/reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll ^
 /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:Microsoft.CSharp.dll

echo Building main (winexe, no console window)...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /warn:4 /win32icon:src\res\app.ico ^
  /out:build\AgoAnchor.exe %REFS% src\*.cs

if errorlevel 1 (
    echo.
    echo [FAILED] main build error
    exit /b 1
)

echo Building diagnostics (console, for --selftest / --gallery / --headless)...
"%CSC%" /nologo /target:exe /platform:anycpu /optimize+ /warn:4 /win32icon:src\res\app.ico ^
  /out:build\AgoAnchor.diag.exe %REFS% src\*.cs

if errorlevel 1 (
    echo.
    echo [FAILED] diagnostics build error
    exit /b 1
)


echo.
echo [OK] build\AgoAnchor.exe       ^(run this^)
echo [OK] build\AgoAnchor.diag.exe  ^(self-test / gallery / headless^)
echo.
echo Self-test: build\AgoAnchor.diag.exe --selftest
exit /b 0


