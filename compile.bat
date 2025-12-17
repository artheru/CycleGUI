@echo off
REM Build CycleGUI libVRender project

echo Building libVRender...
echo.

REM Step 1: Use vswhere to get the Visual Studio installation path
for /f "delims=" %%i in ('"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -property installationPath') do set IDIR=%%i

REM Check if VSINSTALLDIR was found
if "%IDIR%"=="" (
    echo Error: Visual Studio installation not found.
    exit /b 1
)

echo Found Visual Studio at: %IDIR%
echo.

REM Step 2: Locate vcvars64.bat
set VC_CMD="%IDIR%\VC\Auxiliary\Build\vcvars64.bat"

if not exist %VC_CMD% (
    echo Error: vcvars64.bat not found.
    exit /b 1
)

REM Step 3: Call vcvars64.bat to setup environment
call %VC_CMD%

REM Step 4: Build the project using MSBuild
echo.
echo Building CycleGUI.sln...
echo.

REM Build Debug configuration
if "%1"=="Release" (
    echo Building Release configuration...
    msbuild CycleGUI.sln /p:Configuration=LibraryRelease /p:Platform=x64 /m /v:minimal
) else if "%1"=="release" (
    echo Building Release configuration...
    msbuild CycleGUI.sln /p:Configuration=LibraryRelease /p:Platform=x64 /m /v:minimal
) else (
    echo Building Debug configuration...
    msbuild CycleGUI.sln /p:Configuration=LibraryDebug /p:Platform=x64 /m /v:minimal
)

if %errorlevel% neq 0 (
    echo.
    echo Build FAILED with error code %errorlevel%
    exit /b %errorlevel%
)

echo.
echo Build completed successfully!
echo.













