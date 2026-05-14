# External Mods

This folder keeps JetOS-adjacent Space Engineers plugins visible from the JetOS repo.

## Dev

`ExternalMods/Dev` is intentionally gitignored. It contains local Windows junctions to development or runtime folders:

- `LcdBoosterClient` -> `C:\Users\xerdi\source\repos\LcdBoosterClient`
- `PulsarLegacyLocal` -> `%APPDATA%\Pulsar\Legacy\Local`

The active launch command uses `%APPDATA%\Pulsar\Interim.exe`, and the current game log shows Interim loading plugins through the legacy preloader path. The old `LcdBoosterProxy` folder in the Space Engineers install is not linked here.

## Built

`ExternalMods/Built` is tracked on purpose. The DLLs there are hard links to the current built/deployed DLLs so git sees shareable binary contents.

`ExternalMods/Built/Pulsar` is for `%APPDATA%\Pulsar\Interim.exe` / Pulsar local plugin deployment:

- `LcdBoosterClient.dll`
- `CameraLCD-CAMOV.dll`
- `JetOSRadarFeed.dll`

`ExternalMods/Built/Torch` is for Torch dedicated server deployment:

- `JetOSRadarFeedTorch.dll`

Refresh these links after rebuilding or redeploying a plugin if the build tool replaces the target file instead of updating it in place.
