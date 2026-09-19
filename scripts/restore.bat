@echo off
echo Restoring ClaudeTraceHub solution...
call "%~dp0_set-sdk.bat" || (pause & exit /b 1)
cd /d "%~dp0.."
dotnet restore ClaudeTraceHub.sln
echo Restored ClaudeTraceHub solution
pause
