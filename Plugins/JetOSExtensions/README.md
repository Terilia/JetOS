# JetOS Extensions

JetOS Extensions is the consolidated first-release development bundle for the JetOS-side Space Engineers plugins. It keeps the old source projects intact and imports their current local `G:\dev` implementations into one shippable repo folder.

## Runtime Artifacts

- `JetOSExtensions.Client`: Pulsar / Space Engineers client plugin targeting `net9.0`.
- `JetOSExtensions.Server`: Torch server plugin targeting `net48`.
- `Content/JetOSSpriteUnlocker`: copied JetOS LCD sprite/content mod assets from `Mod/testmod`.

## Included Features

- TerrainAPI programmable block terminal property: `TerrainAPI`.
- Radar feed programmable block terminal property: `JetOSRadarFeed`, server-side only.
- CAMOV camera LCD rendering with programmable block sprite overlay preservation.
- Client 60 FPS LCD render patch.
- Server LCD booster send/replication patches.
- `[Ani]` canard angle sync/fix on server and client.
- JetOS LCD sprite definitions and textures.

## Runtime Logging

The client and server write startup logs for loaded features and Harmony patches, plus error logs when feature updates fail. Repeating heartbeat telemetry is disabled for normal use.

## Build And Package

Run from the repo root:

```powershell
dotnet run --project Plugins\JetOSExtensions\JetOSExtensions.Tests\JetOSExtensions.Tests.csproj --configuration Release
dotnet build Plugins\JetOSExtensions\JetOSExtensions.Client\JetOSExtensions.Client.csproj --configuration Release
dotnet build Plugins\JetOSExtensions\JetOSExtensions.Server\JetOSExtensions.Server.csproj --configuration Release
Plugins\JetOSExtensions\package-dev.ps1
```

The package script refreshes:

- `ExternalMods/Built/Pulsar/JetOSExtensions.Client.dll`
- `ExternalMods/Built/Torch/JetOSExtensions.Server.dll`
- `ExternalMods/Built/Torch/JetOSExtensions.Server.pdb`
- `ExternalMods/Built/Torch/JetOSExtensions.Server.zip`
- `ExternalMods/Built/Content/JetOSSpriteUnlocker`
