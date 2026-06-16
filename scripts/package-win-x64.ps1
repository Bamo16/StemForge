#Requires -Version 7
<#
.SYNOPSIS
    Stage the win-x64 publish output under StemForge/ and produce the release zip.
.DESCRIPTION
    Expects publish/win-x64/ to already be populated (run the win-x64 publish tasks first).
    Reads the version from Directory.Build.props and writes
    publish/StemForge-v<version>-win-x64.zip.
#>
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root 'publish/win-x64'
$stage = Join-Path $root 'publish/stage'
$version = (Select-Xml -Path (Join-Path $root 'Directory.Build.props') -XPath '//Version').Node.InnerText
$zip = Join-Path $root "publish/StemForge-v$version-win-x64.zip"

if (-not (Test-Path $publish)) {
    throw "Publish output not found at $publish. Run the win-x64 publish first."
}

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$stageApp = Join-Path $stage 'StemForge'
New-Item -ItemType Directory -Path $stageApp -Force | Out-Null
Copy-Item "$publish/*" $stageApp -Recurse

if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $stageApp -DestinationPath $zip -CompressionLevel Optimal

"Created: $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
