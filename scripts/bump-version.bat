@echo off
setlocal enabledelayedexpansion

set PROPS_FILE=%~dp0..\Directory.Build.props

:: Read current version from Directory.Build.props
for /f "tokens=3 delims=<>" %%a in ('findstr "<Version>" "%PROPS_FILE%"') do (
    set CURRENT=%%a
)

if not defined CURRENT (
    echo ERROR: Could not read current version from Directory.Build.props
    exit /b 1
)

:: Parse Major.Minor.Patch
for /f "tokens=1,2,3 delims=." %%a in ("%CURRENT%") do (
    set MAJOR=%%a
    set MINOR=%%b
    set PATCH=%%c
)

if "%1"=="" goto usage

if "%1"=="major" (
    set /a MAJOR+=1
    set MINOR=0
    set PATCH=0
    goto apply
)
if "%1"=="minor" (
    set /a MINOR+=1
    set PATCH=0
    goto apply
)
if "%1"=="patch" (
    set /a PATCH+=1
    goto apply
)
if "%1"=="current" (
    echo %CURRENT%
    goto end
)
if "%1"=="set" (
    if "%2"=="" (
        echo ERROR: Provide version number. Usage: bump-version.bat set 2.1.0
        exit /b 1
    )
    set NEW=%2
    goto write
)
if "%1"=="auto" goto auto
if "%1"=="tag" goto tag
goto usage

:auto
:: Detect bump type from git commit messages using conventional commits
:: Prefixes: fix/bugfix/hotfix -> patch, feat/feature/add/update -> minor, BREAKING CHANGE/breaking/major -> major
git rev-parse --git-dir >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: Not a git repository.
    exit /b 1
)

:: Check for uncommitted changes
git diff --quiet 2>nul
set HAS_UNSTAGED=%errorlevel%
git diff --cached --quiet 2>nul
set HAS_STAGED=%errorlevel%
if %HAS_UNSTAGED% neq 0 (
    echo WARNING: You have uncommitted changes. Commit first for accurate detection.
    echo.
)
if %HAS_STAGED% neq 0 (
    echo WARNING: You have staged changes. Commit first for accurate detection.
    echo.
)

:: Find last version tag
set LAST_TAG=
for /f "delims=" %%t in ('git tag -l "v*" --sort=-v:refname 2^>nul') do (
    if not defined LAST_TAG set "LAST_TAG=%%t"
)

:: Get commits since last tag (or all commits if no tag)
set BUMP=none
set HAS_MAJOR=0
set HAS_MINOR=0
set HAS_PATCH=0
set COMMIT_COUNT=0

if defined LAST_TAG (
    echo Analyzing commits since %LAST_TAG%...
    set "GIT_RANGE=%LAST_TAG%..HEAD"
) else (
    echo Analyzing all commits ^(no version tag found^)...
    set "GIT_RANGE=HEAD"
)

:: Use PowerShell for reliable commit message parsing
for /f "usebackq delims=" %%r in (`powershell -NoProfile -Command ^
    "$range='!GIT_RANGE!'; " ^
    "$logs = if ($range -eq 'HEAD') { git log --format='%%s' 2>$null } else { git log --format='%%s' $range 2>$null }; " ^
    "if (-not $logs) { Write-Output 'NONE:0:0:0'; exit }; " ^
    "$major=0; $minor=0; $patch=0; $count=0; " ^
    "foreach ($msg in $logs) { " ^
    "  $count++; " ^
    "  $lower = $msg.ToLower().Trim(); " ^
    "  if ($lower -match '^breaking[\s:]' -or $lower -match '^major[\s:]' -or $msg -match 'BREAKING CHANGE' -or $lower -match '^^!:') { $major++ } " ^
    "  elseif ($lower -match '^feat[\s:\(]' -or $lower -match '^feature[\s:\(]' -or $lower -match '^add[\s:\(]' -or $lower -match '^update[\s:\(]' -or $lower -match '^enhance[\s:\(]') { $minor++ } " ^
    "  elseif ($lower -match '^fix[\s:\(]' -or $lower -match '^bugfix[\s:\(]' -or $lower -match '^hotfix[\s:\(]' -or $lower -match '^patch[\s:\(]' -or $lower -match '^perf[\s:\(]') { $patch++ } " ^
    "  else { $patch++ } " ^
    "}; " ^
    "Write-Output \"$count`:$major`:$minor`:$patch\""`) do (
    for /f "tokens=1,2,3,4 delims=:" %%a in ("%%r") do (
        set COMMIT_COUNT=%%a
        set HAS_MAJOR=%%b
        set HAS_MINOR=%%c
        set HAS_PATCH=%%d
    )
)

if "%COMMIT_COUNT%"=="0" (
    echo No new commits found since last version. Nothing to bump.
    goto end
)
if "%COMMIT_COUNT%"=="NONE" (
    echo No commits found. Nothing to bump.
    goto end
)

:: Determine bump type (highest wins)
if %HAS_MAJOR% gtr 0 (
    set BUMP=major
    set /a MAJOR+=1
    set MINOR=0
    set PATCH=0
) else if %HAS_MINOR% gtr 0 (
    set BUMP=minor
    set /a MINOR+=1
    set PATCH=0
) else (
    set BUMP=patch
    set /a PATCH+=1
)

echo.
echo Commits analyzed: %COMMIT_COUNT%
echo   Breaking/major: %HAS_MAJOR%
echo   Features/minor: %HAS_MINOR%
echo   Fixes/patch:    %HAS_PATCH%
echo.
echo Detected bump type: %BUMP%
goto apply

:tag
:: Create a git version tag for the current version
git rev-parse --git-dir >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: Not a git repository.
    exit /b 1
)
git rev-parse HEAD >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: No commits found. Commit first before tagging.
    exit /b 1
)
git tag "v%CURRENT%" 2>nul
if %errorlevel% neq 0 (
    echo Tag v%CURRENT% already exists.
) else (
    echo Tagged current commit as v%CURRENT%
)
goto end

:apply
set NEW=!MAJOR!.!MINOR!.!PATCH!

:write
echo Bumping version: %CURRENT% -^> !NEW!

:: Replace version in Directory.Build.props using PowerShell for reliability
powershell -NoProfile -Command "(Get-Content '%PROPS_FILE%') -replace '<Version>%CURRENT%</Version>', '<Version>!NEW!</Version>' | Set-Content '%PROPS_FILE%'"

if %errorlevel% neq 0 (
    echo ERROR: Failed to update version.
    exit /b 1
)

echo Version updated to !NEW!
goto end

:usage
echo.
echo ClaudeTraceHub Version Manager
echo.
echo Current version: %CURRENT%
echo.
echo Usage: bump-version.bat [command]
echo.
echo Commands:
echo   auto       Auto-detect bump type from git commit messages
echo   major      Bump major version  (breaking changes)
echo   minor      Bump minor version  (new features)
echo   patch      Bump patch version  (bug fixes, hotfixes)
echo   set x.y.z  Set explicit version
echo   current    Show current version
echo   tag        Create git tag (v%CURRENT%) for current version
echo.
echo Commit message prefixes for auto-detection:
echo   major/patch  ^| breaking:, major:, BREAKING CHANGE
echo   minor/feature^| feat:, feature:, add:, update:, enhance:
echo   patch/fix    ^| fix:, bugfix:, hotfix:, patch:, perf:
echo.
echo Examples:
echo   bump-version.bat auto            Detect and bump from git history
echo   bump-version.bat patch           Bug fix / hotfix
echo   bump-version.bat minor           New feature release
echo   bump-version.bat major           Breaking change release
echo   bump-version.bat set 2.0.0       Set specific version
echo   bump-version.bat tag             Tag current commit with version
echo.

:end
endlocal
pause
