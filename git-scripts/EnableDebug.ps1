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
$sourcePix   = Join-Path $sourceDir "WinPixEventRuntime.dll"

Write-Host "Enabling Debug Mode for KSP..."

# Delete existing UnityPlayer.dll
if (Test-Path $targetUnity) {
    Remove-Item $targetUnity -Force
    Write-Host "Deleted existing UnityPlayer.dll"
} else {
    Write-Host "UnityPlayer.dll not found, skipping delete"
}

# Copy UnityPlayer.dll
Copy-Item $sourceUnity -Destination $targetDir -Force
Write-Host "Copied UnityPlayer.dll"

# Copy WinPixEventRuntime.dll
Copy-Item $sourcePix -Destination $targetDir -Force
Write-Host "Copied WinPixEventRuntime.dll"

# Append player-connection-debug=1 if not already present
if (Test-Path $bootConfig) {
    $content = Get-Content $bootConfig

    if ($content -notcontains "player-connection-debug=1") {
        Add-Content $bootConfig "player-connection-debug=1"
        Write-Host "Added player-connection-debug=1 to boot.config"
    } else {
        Write-Host "boot.config already contains player-connection-debug=1"
    }
} else {
    Write-Host "boot.config not found: $bootConfig"
}

Write-Host "KSP Debug Mode disabled."