@echo off
echo Cleaning ClaudeTraceHub solution...
set MSBuildSDKsPath=C:\Program Files\dotnet\sdk\9.0.312\Sdks
cd /d "%~dp0.."
dotnet clean ClaudeTraceHub.sln
echo Cleaned ClaudeTraceHub solution
pause
