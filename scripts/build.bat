@echo off
echo Building ClaudeTraceHub solution...
call "%~dp0_set-sdk.bat" || (pause & exit /b 1)
cd /d "%~dp0.."
dotnet build ClaudeTraceHub.sln
echo Built ClaudeTraceHub solution
pause
