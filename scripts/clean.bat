@echo off
echo Cleaning ClaudeTraceHub solution...
set MSBuildSDKsPath=C:\Program Files\dotnet\sdk\9.0.313\Sdks
cd /d "%~dp0.."
dotnet clean ClaudeTraceHub.sln
echo Cleaned ClaudeTraceHub solution
pause
