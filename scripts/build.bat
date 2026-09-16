@echo off
echo Building ClaudeTraceHub solution...
set MSBuildSDKsPath=C:\Program Files\dotnet\sdk\10.0.401\Sdks
cd /d "%~dp0.."
dotnet build ClaudeTraceHub.sln
echo Built ClaudeTraceHub solution
pause
