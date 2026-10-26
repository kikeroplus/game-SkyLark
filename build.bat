@echo off
rem Build SKYLARK with the built-in .NET Framework 4 compiler (no SDK needed)
setlocal
cd /d "%~dp0"
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set SRC=Program.cs Game.cs GameDraw.cs GameDraw25.cs GameDrawB.cs GameStory.cs GameRegions.cs Gfx.cs Sound.cs
set REFS=/r:System.Windows.Forms.dll /r:System.Drawing.dll

rem First build only: make SkyLark.ico with the exe itself
if not exist SkyLark.ico (
  "%FW%\csc.exe" /nologo /target:winexe /optimize+ /platform:anycpu /out:SkyLark.exe %REFS% %SRC%
  if errorlevel 1 goto fail
  "%~dp0SkyLark.exe" --make-icon SkyLark.ico
)

"%FW%\csc.exe" /nologo /target:winexe /optimize+ /platform:anycpu /out:SkyLark.exe /win32icon:SkyLark.ico %REFS% %SRC%
if errorlevel 1 goto fail
echo BUILD OK: SkyLark.exe
exit /b 0
:fail
echo BUILD FAILED
exit /b 1
