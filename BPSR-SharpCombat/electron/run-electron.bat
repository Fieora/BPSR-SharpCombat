@echo off
REM Run the cross-platform Node build script and start electron in dev mode
node "%~dp0build.js" --devStart
pause

