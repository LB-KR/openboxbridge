@echo off
rem OpenBoxBridge - UP300 diagnostics. Read-only: nothing moves and no settings are changed.
cd /d "%~dp0"
echo Reading the printer... (close UP Studio and Wand first)
set OUT=up300-diagnostics.txt
echo OpenBoxBridge UP300 diagnostics > %OUT%
echo %DATE% %TIME% >> %OUT%
echo. >> %OUT%
echo ===== up3dinfo ===== >> %OUT%
bin\up3dinfo.exe >> %OUT% 2>&1
echo. >> %OUT%
echo ===== up3dstatus ===== >> %OUT%
bin\up3dstatus.exe >> %OUT% 2>&1
echo. >> %OUT%
echo ===== up3dsnap ===== >> %OUT%
bin\up3dsnap.exe up300-snapshot.txt >> %OUT% 2>&1
echo.
echo Done. Please send these two files:
echo   %CD%\up300-diagnostics.txt
echo   %CD%\up300-snapshot.txt
pause
