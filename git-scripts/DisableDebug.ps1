# put this in .git

param(
    [Parameter(Mandatory = $true)]
    [string]$targetDir,

    [Parameter(Mandatory = $true)]
    [string]$sourceDir
)

$bootConfig  = Join-Path $targetDir "KSP_x64_Data\boot.config"

$targetUnity = Join-Path $targetDir "UnityPlayer.dll"
$sourceUnity = Join-Path $sourceDir "UnityPlayer.dll"
$targetPix   = Join-Path $targetDir "WinPixEventRuntime.dll"

Write-Host "Disabling Debug Mode for KSP..."

# Delete existing UnityPlayer.dll
if (Test-Path $targetUnity) {
    Remove-Item $targetUnity -Force
    Write-Host "Deleted existing UnityPlayer.dll"
} else {
    Write-Host "UnityPlayer.dll not found, skipping delete"
}

# Copy original UnityPlayer.dll
Copy-Item $sourceUnity -Destination $targetDir -Force
Write-Host "Copied original UnityPlayer.dll"

# Delete WinPixEventRuntime.dll
if (Test-Path $targetPix) {
    Remove-Item $targetPix -Force
    Write-Host "Deleted WinPixEventRuntime.dll"
} else {
    Write-Host "WinPixEventRuntime.dll not found, skipping delete"
}

# Remove player-connection-debug=1 from boot.config
if (Test-Path $bootConfig) {
    $content = Get-Content $bootConfig
    $newContent = $content | Where-Object { $_.Trim() -ne "player-connection-debug=1" }

    Set-Content $bootConfig $newContent
    Write-Host "Removed player-connection-debug=1 from boot.config (if present)"
} else {
    Write-Host "boot.config not found: $bootConfig"
}

Write-Host "KSP Debug Mode enabled."