#Requires -Version 7
<#
.SYNOPSIS
    Copy the win-x64 publish output to the folder named by STEMFORGE_INSTALL_DIR.
.DESCRIPTION
    Expects publish/win-x64/ to already be populated (the "install: win-x64" task publishes first).
    The destination is the user's own, so it comes from an environment variable rather than the
    repo. Refuses while a StemForge process is running, since its executables would be locked.
#>
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root 'publish/win-x64'
$destination = $env:STEMFORGE_INSTALL_DIR

if (-not $destination) {
    throw 'Set STEMFORGE_INSTALL_DIR to the folder to install into, for example: setx STEMFORGE_INSTALL_DIR "C:\Tools\StemForge"'
}
if (-not (Test-Path $publish)) {
    throw "Publish output not found at $publish. Run the win-x64 publish first."
}
if (Get-Process -Name 'StemForge', 'stemforge-cli' -ErrorAction SilentlyContinue) {
    throw 'StemForge is running. Close it and try again.'
}

New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item "$publish/*" $destination -Recurse -Force

$version = (Get-Item (Join-Path $destination 'stemforge-cli.exe')).VersionInfo.ProductVersion
"Installed StemForge $version to $destination"
