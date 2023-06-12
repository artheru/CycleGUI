@echo off
setlocal enabledelayedexpansion

set SOKOL_SHDC=sokol-shdc
set SLANG=glsl300es

REM Iterate through each .glsl file in the directory
for %%f in (*.glsl) do (
    set FILENAME=%%~nf
    %SOKOL_SHDC% --input "%%~f" --output "!FILENAME!.h" --slang %SLANG%
)

echo Compilation complete!