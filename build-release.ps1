# GenHub Test Release Build Script
# This script builds and packages GenHub as version 0.0.0 for local testing.

$ErrorActionPreference = "Stop"

$version = "0.0.3"
$configuration = "Release"
$runtime = "win-x64"
$projectPath = "GenHub/GenHub.Windows/GenHub.Windows.csproj"
$publishDir = "win-publish-test"
$outputDir = "Releases"
$appName = "GenHub"
$authors = "Community Outpost"
$iconPath = "GenHub/GenHub/Assets/Icons/generalshub.ico"

Write-Host "--- GenHub Test Build v$version ---" -ForegroundColor Cyan

# 1. Cleanup
if (Test-Path $publishDir) {
    Write-Host "Cleaning up old publish directory..."
    Remove-Item -Path $publishDir -Recurse -Force
}
if (Test-Path $outputDir) {
    Write-Host "Cleaning up old output directory..."
    Remove-Item -Path $outputDir -Recurse -Force
}

# 2. Check for Velopack CLI (vpk)
Write-Host "Checking for Velopack CLI (vpk)..."
try {
    & vpk --help | Out-Null
} catch {
    Write-Error "Velopack CLI (vpk) not found. Please install it with: dotnet tool install -g vpk"
}

# 3. Publish Windows App
Write-Host "Publishing Windows application..." -ForegroundColor Green
dotnet publish $projectPath `
    -c $configuration `
    -r $runtime `
    --self-contained true `
    -p:Version=$version `
    -p:BuildChannel="Test" `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Dotnet publish failed."
}

# 4. Create Velopack Package
Write-Host "Creating Velopack package..." -ForegroundColor Green
$tempPackDir = "temp-pack-output"
if (Test-Path $tempPackDir) { Remove-Item -Path $tempPackDir -Recurse -Force }
New-Item -ItemType Directory -Path $tempPackDir | Out-Null

if (!(Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

& vpk pack `
    --packId $appName `
    --packVersion $version `
    --packDir $publishDir `
    --mainExe "$appName.Windows.exe" `
    --packTitle $appName `
    --packAuthors $authors `
    --icon $iconPath `
    --outputDir $tempPackDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Velopack pack failed."
}

# 5. Extract only Setup.exe and Cleanup
Write-Host "Cleaning up and extracting Setup.exe..." -ForegroundColor Yellow
$setupExe = Get-ChildItem -Path $tempPackDir -Filter "*Setup.exe" | Select-Object -First 1
if ($null -ne $setupExe) {
    Move-Item -Path $setupExe.FullName -Destination "$outputDir\GenHub-Setup.exe" -Force
    Write-Host "Success: GenHub-Setup.exe is ready in $outputDir" -ForegroundColor Green
} else {
    Write-Error "Could not find Setup.exe in Velopack output."
}

# Cleanup temporary directories
Remove-Item -Path $tempPackDir -Recurse -Force
Remove-Item -Path $publishDir -Recurse -Force

Write-Host ""
Write-Host "--- Build Complete! ---" -ForegroundColor Cyan
Write-Host "Installer: $outputDir\GenHub-Setup.exe"
Write-Host "Version: $version"
Write-Host ""

