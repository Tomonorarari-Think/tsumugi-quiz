@echo off
rem TsumugiQuiz: install the Python libraries (psd-tools, Pillow) used to make the
rem character expression images (issue #219). Run once after installing Python.
rem Keep this file ASCII only (cmd.exe reads it with the console code page).
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0make-expressions.ps1" -InstallDepsOnly %*
set "TQ_EXIT_CODE=%ERRORLEVEL%"
echo.
pause
exit /b %TQ_EXIT_CODE%
