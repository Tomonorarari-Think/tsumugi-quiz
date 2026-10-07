@echo off
rem TsumugiQuiz: make the character expression images (issue #219).
rem Drag and drop the official character zip onto this file, or double-click it
rem (then the zip is searched for in the Downloads folder).
rem Keep this file ASCII only (cmd.exe reads it with the console code page).
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0make-expressions.ps1" %*
set "TQ_EXIT_CODE=%ERRORLEVEL%"
echo.
pause
exit /b %TQ_EXIT_CODE%
