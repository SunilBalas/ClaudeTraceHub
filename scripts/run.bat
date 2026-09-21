@echo off
echo Running ClaudeTraceHub application...
call "%~dp0_set-sdk.bat" || (pause & exit /b 1)
cd /d "%~dp0.."
dotnet run --project ClaudeTraceHub.Web --urls "http://localhost:5110;https://localhost:5111"
echo.
echo App exited with code %ERRORLEVEL%
pause
