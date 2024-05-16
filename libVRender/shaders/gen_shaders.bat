@echo off
setlocal enabledelayedexpansion

set SOKOL_SHDC=sokol-shdc
set SLANG=glsl300es

REM Iterate through each .glsl file in the directory
for %%f in (*.glsl) do (
    set FILENAME=%%~nf
    %SOKOL_SHDC% --input "%%~f" --output "!FILENAME!.h" --slang %SLANG%
)

> shaders.h echo // Auto-generated shader.h file
>> shaders.h echo.

for %%f in (*.h) do (
    if not "%%f"=="shaders.h" (
        >> shaders.h echo #include "%%f"
    )
)

echo shaders.h has been generated.
echo Compilation complete!