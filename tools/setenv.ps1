# Check Refasmer
$toolName = "Refasmer"
$toolPath = (Get-Command $toolName -ErrorAction SilentlyContinue).Source

if (!$toolPath) {
    Write-Output "Installing $toolName..."
    dotnet tool install -g JetBrains.Refasmer.CliTool
} else {
    Write-Output "$toolName already installed."
}

# Create the destination directory if it does not exist
if (-Not (Test-Path -Path "deps")) {
    New-Item -ItemType Directory -Path "deps"
}

# Use Invoke-WebRequest to download the file
Invoke-WebRequest -Uri "https://mdcs.lessokaji.com/dependencies/RefLessokajiProtect.dll" -OutFile "deps/RefLessokajiProtect.dll"
Invoke-WebRequest -Uri "https://mdcs.lessokaji.com/dependencies/RefFundamentalLib.dll" -OutFile "deps/RefFundamentalLib.dll"
Invoke-WebRequest -Uri "https://mdcs.lessokaji.com/dependencies/LessokajiWeaverUtilities.dll" -OutFile "deps/LessokajiWeaverUtilities.dll"
Invoke-WebRequest -Uri "https://mdcs.lessokaji.com/dependencies/LessokajiWeaver.Fody.dll" -OutFile "deps/LessokajiWeaver.Fody.dll"

