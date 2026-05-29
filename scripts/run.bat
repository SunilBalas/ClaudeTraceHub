@echo off
echo Running ClaudeTraceHub application...
set MSBuildSDKsPath=C:\Program Files\dotnet\sdk\9.0.314\Sdks
cd /d "%~dp0.."
dotnet run --project ClaudeTraceHub.Web --urls "http://localhost:5110;https://localhost:5111"
echo.
echo App exited with code %ERRORLEVEL%
pause
