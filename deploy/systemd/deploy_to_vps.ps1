$ErrorActionPreference = "Stop"

$VpsUser = "root"
$VpsHost = "31.56.56.8"
$RemoteAppDir = "/root/project-multiplayer-server"
$LocalBuildDir = "C:\Users\carlo\OneDrive\Documents\Github Project\Unity\Server\Project Multiplayer 1.0.1"
$LocalUnitFile = "C:\Users\carlo\OneDrive\Documents\Github Project\Unity\ProjectMultiplayer\deploy\systemd\projectmultiplayer.service"
$LocalInstallScript = "C:\Users\carlo\OneDrive\Documents\Github Project\Unity\ProjectMultiplayer\deploy\systemd\install_on_vps.sh"

Write-Host "1) Create remote app dir..."
ssh "$VpsUser@$VpsHost" "mkdir -p $RemoteAppDir"

Write-Host "2) Upload Linux server build..."
scp -r "$LocalBuildDir\*" "$VpsUser@$VpsHost`:$RemoteAppDir/"

Write-Host "3) Upload systemd files..."
scp "$LocalUnitFile" "$VpsUser@$VpsHost`:$RemoteAppDir/projectmultiplayer.service"
scp "$LocalInstallScript" "$VpsUser@$VpsHost`:$RemoteAppDir/install_on_vps.sh"

Write-Host "4) Install and start service..."
ssh "$VpsUser@$VpsHost" "chmod +x $RemoteAppDir/ServerProjectMultiplayer.x86_64 $RemoteAppDir/install_on_vps.sh && $RemoteAppDir/install_on_vps.sh"

Write-Host "Done. Service should be active now."
