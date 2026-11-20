@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Missing 64-bit .NET Framework 4.x compiler.
  exit /b 1
)
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /utf8output /out:ZStage.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll src\Program.cs src\Controller.cs src\Tests.cs
if errorlevel 1 exit /b 1
echo Built ZStage.exe
exit /b 0
