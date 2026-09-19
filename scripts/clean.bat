@echo off
echo Cleaning ClaudeTraceHub solution...
call "%~dp0_set-sdk.bat" || (pause & exit /b 1)
cd /d "%~dp0.."
dotnet clean ClaudeTraceHub.sln
echo Cleaned ClaudeTraceHub solution
pause
