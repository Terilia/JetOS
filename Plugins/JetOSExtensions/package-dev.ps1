[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$pluginRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $pluginRoot "..\..")
$clientProject = Join-Path $pluginRoot "JetOSExtensions.Client\JetOSExtensions.Client.csproj"
$serverProject = Join-Path $pluginRoot "JetOSExtensions.Server\JetOSExtensions.Server.csproj"
$contentSource = Join-Path $pluginRoot "Content\JetOSSpriteUnlocker"

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Command,
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

Invoke-Checked { dotnet build $clientProject --configuration $Configuration } "client build"
Invoke-Checked { dotnet build $serverProject --configuration $Configuration } "server build"

$pulsarOut = Join-Path $repoRoot "ExternalMods\Built\Pulsar"
$torchOut = Join-Path $repoRoot "ExternalMods\Built\Torch"
$contentOut = Join-Path $repoRoot "ExternalMods\Built\Content\JetOSSpriteUnlocker"
New-Item -ItemType Directory -Force -Path $pulsarOut, $torchOut, (Split-Path -Parent $contentOut) | Out-Null

$clientDll = Join-Path $pluginRoot "JetOSExtensions.Client\bin\$Configuration\net9.0\JetOSExtensions.Client.dll"
$serverDll = Join-Path $pluginRoot "JetOSExtensions.Server\bin\$Configuration\net48\JetOSExtensions.Server.dll"
$serverPdb = Join-Path $pluginRoot "JetOSExtensions.Server\bin\$Configuration\net48\JetOSExtensions.Server.pdb"
$serverManifest = Join-Path $pluginRoot "JetOSExtensions.Server\manifest.xml"

Copy-Item -LiteralPath $clientDll -Destination (Join-Path $pulsarOut "JetOSExtensions.Client.dll") -Force
Copy-Item -LiteralPath $serverDll -Destination (Join-Path $torchOut "JetOSExtensions.Server.dll") -Force
Copy-Item -LiteralPath $serverPdb -Destination (Join-Path $torchOut "JetOSExtensions.Server.pdb") -Force

if (Test-Path -LiteralPath $contentOut) {
    Remove-Item -LiteralPath $contentOut -Recurse -Force
}
Copy-Item -LiteralPath $contentSource -Destination $contentOut -Recurse -Force

$stage = Join-Path ([System.IO.Path]::GetTempPath()) "JetOSExtensions.Server.zipbuild"
if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item -LiteralPath $serverManifest -Destination (Join-Path $stage "manifest.xml") -Force
Copy-Item -LiteralPath $serverDll -Destination (Join-Path $stage "JetOSExtensions.Server.dll") -Force
Copy-Item -LiteralPath $serverPdb -Destination (Join-Path $stage "JetOSExtensions.Server.pdb") -Force

$zip = Join-Path $torchOut "JetOSExtensions.Server.zip"
if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal

Get-FileHash -Algorithm SHA256 `
    (Join-Path $pulsarOut "JetOSExtensions.Client.dll"), `
    (Join-Path $torchOut "JetOSExtensions.Server.dll"), `
    (Join-Path $torchOut "JetOSExtensions.Server.zip")
