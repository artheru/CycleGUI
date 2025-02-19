# powershell -ExecutionPolicy Bypass -File .\ComputeTotalSHA1.ps1

# Path to the file containing the list of files.
$codesFile = "./../libVRender/renderer_codes.txt"

if (-not (Test-Path $codesFile)) {
    Write-Error "List file '$codesFile' not found. Make sure it exists."
    exit 1
}

# Get the directory of renderer_codes.txt so that file paths are interpreted correctly.
$baseDir = Split-Path -Path $codesFile -Parent
if ([string]::IsNullOrEmpty($baseDir)) {
    $baseDir = Get-Location
}

# Create a SHA1 instance.
$sha1 = [System.Security.Cryptography.SHA1]::Create()

# Set a suitable buffer size (here, 4 MB)
$bufferSize = 14 * 1024 * 1024

# Process each file in renderer_codes.txt in sequence.
foreach ($line in Get-Content $codesFile) {
    $fileRelative = $line.Trim()
    
    # Skip blank lines.
    if ([string]::IsNullOrWhiteSpace($fileRelative)) { continue }

    # Compute the full path of the file (relative to the directory of renderer_codes.txt)
    $file = Join-Path $baseDir $fileRelative

    if (-not (Test-Path $file)) {
        Write-Warning "File not found: $file"
        continue
    }

    try {
        $fs = [System.IO.File]::OpenRead($file)
    } catch {
        Write-Warning "Unable to open file: $file"
        continue
    }
    
    try {
        $buffer = New-Object byte[] $bufferSize
        while (($bytesRead = $fs.Read($buffer, 0, $bufferSize)) -gt 0) {
            # Feed each block into the hash algorithm.
            $sha1.TransformBlock($buffer, 0, $bytesRead, $null, 0) | Out-Null
        }
    } finally {
        $fs.Close()
    }
}

# Finalize the hash computation.
$emptyBuffer = New-Object byte[] 0
$sha1.TransformFinalBlock($emptyBuffer, 0, 0) | Out-Null

# Retrieve the final hash as a byte array and convert last 4 bytes to a hexadecimal string
$hashBytes = $sha1.Hash
$lastFourBytes = $hashBytes[-2..-1]  # Get last 4 bytes
$hashString = [System.BitConverter]::ToString($lastFourBytes) -replace "-", ""

Write-Output "#define LIB_VERSION 0x$hashString"
