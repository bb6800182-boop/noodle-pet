@echo off
rem Build dist\noodle-pet.exe with the C# compiler that ships with Windows (.NET Framework 4.8). Nothing to install.
rem   build.cmd           -> dist\noodle-pet.exe
rem   build.cmd snapshot  -> dist\noodle-snapshot.exe (dev tool: renders the settings window and bubbles to PNG for checking)
setlocal EnableDelayedExpansion
cd /d "%~dp0"
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
set "WPF=%FW%\WPF"
set "RSP=%TEMP%\noodle-pet-build.rsp"
if not exist dist mkdir dist

if /i "%~1"=="snapshot" (
  > "%RSP%" echo /main:NoodlePet.Snapshot /out:dist\noodle-snapshot.exe
) else (
  taskkill /im noodle-pet.exe /f >nul 2>&1
  > "%RSP%" echo /main:NoodlePet.Program /out:dist\noodle-pet.exe
)
>>"%RSP%" echo /nologo /target:winexe /optimize+ /codepage:65001 /win32icon:assets\icon.ico
>>"%RSP%" echo /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\WindowsBase.dll"
>>"%RSP%" echo /r:"%FW%\System.Xaml.dll" /r:"%FW%\System.Web.Extensions.dll" /r:"%WPF%\UIAutomationProvider.dll" /r:"%WPF%\UIAutomationTypes.dll"
>>"%RSP%" echo /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll
>>"%RSP%" echo /resource:assets\exercises.json,exercises.json /resource:src\Bubble.xaml,Bubble.xaml /resource:src\Theme.xaml,Theme.xaml /resource:gas\calendar-relay.gs,calendar-relay.gs
for %%f in (assets\tray-*.png) do >>"%RSP%" echo /resource:%%f,%%~nxf
for /d %%c in (assets\sprites\*) do (
  for %%f in (%%c\*.png %%c\*.json) do >>"%RSP%" echo /resource:%%f,%%~nxc.%%~nxf
)
>>"%RSP%" echo src\*.cs
if /i "%~1"=="snapshot" >>"%RSP%" echo tools\Snapshot.cs

"%FW%\csc.exe" @"%RSP%"
if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)
echo BUILD OK
