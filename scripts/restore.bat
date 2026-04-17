@echo off
echo Restoring ClaudeTraceHub solution...
set MSBuildSDKsPath=C:\Program Files\dotnet\sdk\9.0.312\Sdks
cd /d "%~dp0.."
dotnet restore ClaudeTraceHub.sln
echo Restored ClaudeTraceHub solution
pause
