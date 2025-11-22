# Check if shaders have changed and regenerate if needed
$shadersDir = Join-Path $PSScriptRoot "..\libVRender\shaders"
$sumFile = Join-Path $shadersDir "sum.txt"
$genScript = Join-Path $shadersDir "gen_shaders.bat"

# Get all .glsl files and compute combined hash
$glslFiles = Get-ChildItem -Path $shadersDir -Filter "*.glsl" | Sort-Object Name
if ($glslFiles.Count -eq 0) {
    Write-Host "No .glsl files found in shaders directory"
    exit 0
}

# Compute SHA1 hash of all .glsl files combined
$sha1 = New-Object System.Security.Cryptography.SHA1CryptoServiceProvider
$combinedStream = New-Object System.IO.MemoryStream

foreach ($file in $glslFiles) {
    $fileBytes = [System.IO.File]::ReadAllBytes($file.FullName)
    $combinedStream.Write($fileBytes, 0, $fileBytes.Length)
}

$combinedStream.Position = 0
$hashBytes = $sha1.ComputeHash($combinedStream)
$currentHash = [System.BitConverter]::ToString($hashBytes).Replace("-", "").ToLower()
$combinedStream.Close()

# Read existing hash if it exists
$existingHash = ""
if (Test-Path $sumFile) {
    $existingHash = (Get-Content $sumFile -Raw).Trim()
}

# Compare hashes
if ($currentHash -ne $existingHash) {
    Write-Host "Shader files changed (hash: $currentHash). Running gen_shaders.bat..."
    
    # Run gen_shaders.bat
    Push-Location $shadersDir
    try {
        & cmd.exe /c "gen_shaders.bat"
        if ($LASTEXITCODE -ne 0) {
            Write-Error "gen_shaders.bat failed with exit code $LASTEXITCODE"
            Pop-Location
            exit $LASTEXITCODE
        }
    } finally {
        Pop-Location
    }
    
    # Save new hash
    Set-Content -Path $sumFile -Value $currentHash -NoNewline
    Write-Host "Shaders regenerated and hash saved to sum.txt"
} else {
    Write-Host "Shader files unchanged (hash: $currentHash). Skipping generation."
}

exit 0


