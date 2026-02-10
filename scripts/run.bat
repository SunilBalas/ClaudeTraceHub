@echo off
echo Running ClaudeTraceHub application...
set MSBuildSDKsPath=C:\Program Files\dotnet\sdk\9.0.308\Sdks
cd /d "%~dp0.."
dotnet run --project ClaudeTraceHub.Web --urls "http://localhost:5000;https://localhost:5001"
echo.
echo App exited with code %ERRORLEVEL%
pause
