@echo off
setlocal

set PORT=5000
set STARTUP_DIR=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup
set STARTUP_LINK=%STARTUP_DIR%\ClaudeTraceHub.bat

:: Read version from Directory.Build.props
set VERSION=unknown
set PROPS_FILE=%~dp0Directory.Build.props
if exist "%PROPS_FILE%" (
    for /f "tokens=3 delims=<>" %%a in ('findstr "<Version>" "%PROPS_FILE%"') do set VERSION=%%a
)

:: Detect if running from source (has .csproj) or from published folder (has .exe)
set SOURCE_DIR=%~dp0ClaudeTraceHub.Web
set APP_DIR=%~dp0publish
if exist "%~dp0ClaudeTraceHub.Web.exe" (
    :: Running from published folder
    set EXE_PATH=%~dp0ClaudeTraceHub.Web.exe
    set IS_PUBLISHED=1
) else (
    :: Running from source folder
    set EXE_PATH=%APP_DIR%\ClaudeTraceHub.Web.exe
    set IS_PUBLISHED=0
)

if "%1"=="publish" goto publish
if "%1"=="run" goto run
if "%1"=="autostart" goto autostart
if "%1"=="remove" goto remove
if "%1"=="status" goto status
if "%1"=="version" goto version
goto usage

:publish
if "%IS_PUBLISHED%"=="1" (
    echo ERROR: Already in a published folder. Nothing to publish.
    exit /b 1
)
:: Auto-detect .NET 9 SDK via dotnet --list-sdks
for /f "tokens=1,* delims= " %%a in ('dotnet --list-sdks 2^>nul') do (
    echo %%a | findstr /b "9." >nul && (
        set "SDK_VER=%%a"
        set "SDK_PATH=%%b"
    )
)
if not defined SDK_VER (
    echo ERROR: .NET 9 SDK not found. Install from https://dot.net/download
    exit /b 1
)
:: SDK_PATH has brackets e.g. [C:\Program Files\dotnet\sdk] - strip them
set "SDK_PATH=%SDK_PATH:~1,-1%"
set "MSBuildSDKsPath=%SDK_PATH%\%SDK_VER%\Sdks"
echo Publishing ClaudeTraceHub...
echo Using SDK: %MSBuildSDKsPath%
dotnet publish "%SOURCE_DIR%\ClaudeTraceHub.Web.csproj" -c Release -r win-x64 --self-contained -o "%APP_DIR%"
if %errorlevel% neq 0 (
    echo ERROR: Publish failed.
    exit /b 1
)
copy "%~f0" "%APP_DIR%\tracehub.bat" >nul
echo Published to %APP_DIR%
echo.
echo Share the "%APP_DIR%" folder with other users.
echo They just need to run: tracehub.bat run
goto end

:run
if not exist "%EXE_PATH%" (
    echo ERROR: ClaudeTraceHub.Web.exe not found.
    if "%IS_PUBLISHED%"=="0" echo Run "tracehub.bat publish" first.
    exit /b 1
)
echo Starting ClaudeTraceHub on http://localhost:%PORT%
echo Press Ctrl+C to stop.
"%EXE_PATH%" --urls=http://localhost:%PORT%
goto end

:autostart
if not exist "%EXE_PATH%" (
    echo ERROR: ClaudeTraceHub.Web.exe not found.
    if "%IS_PUBLISHED%"=="0" echo Run "tracehub.bat publish" first.
    exit /b 1
)
echo Adding ClaudeTraceHub to Windows Startup folder...
(
    echo @echo off
    echo start "" /min "%EXE_PATH%" --urls=http://localhost:%PORT%
) > "%STARTUP_LINK%"
if %errorlevel% neq 0 (
    echo ERROR: Failed to create startup entry.
    exit /b 1
)
echo.
echo Done! ClaudeTraceHub will auto-start (minimized) on Windows logon.
echo Access at: http://localhost:%PORT%
echo.
echo To start it now, run: tracehub.bat run
goto end

:remove
if exist "%STARTUP_LINK%" (
    del "%STARTUP_LINK%"
    echo Startup entry removed.
) else (
    echo No startup entry found.
)
goto end

:status
echo.
echo === Startup Entry ===
if exist "%STARTUP_LINK%" (
    echo REGISTERED - %STARTUP_LINK%
) else (
    echo NOT registered for auto-start
)
echo.
echo === Process ===
tasklist /fi "imagename eq ClaudeTraceHub.Web.exe" 2>nul | find /i "ClaudeTraceHub" >nul
if %errorlevel%==0 (
    echo ClaudeTraceHub is RUNNING
    echo Access at: http://localhost:%PORT%
) else (
    echo ClaudeTraceHub is NOT running
)
goto end

:version
echo %VERSION%
goto end

:usage
echo.
echo Claude TraceHub v%VERSION%
echo.
echo Usage: tracehub.bat [command]
echo.
echo Commands:
if "%IS_PUBLISHED%"=="0" echo   publish    Build and publish the app to the publish folder
echo   run        Start ClaudeTraceHub now (foreground, Ctrl+C to stop)
echo   autostart  Add to Windows Startup folder (no admin needed)
echo   remove     Remove from Windows Startup folder
echo   status     Check if registered and running
echo   version    Show current version
echo.

:end
endlocal
