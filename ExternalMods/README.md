# External Mods

This folder keeps JetOS-adjacent Space Engineers deployment artifacts visible from the JetOS repo.

## Dev

`ExternalMods/Dev` is intentionally gitignored. It is for local Windows junctions to development or runtime folders, such as `%APPDATA%\Pulsar\Legacy\Local`.

## Built

`ExternalMods/Built` contains the current JetOS deployment artifacts.

`ExternalMods/Built/Pulsar` is for Pulsar local plugin deployment:

- `JetOSExtensions.Client.dll`
- `0Harmony.dll`

`ExternalMods/Built/Torch` is for Torch dedicated server deployment:

- `JetOSExtensions.Server.zip`
- `JetOSExtensions.Server.dll`
- `0Harmony.dll`

Use the combined JetOS Extensions bundle for new installs. Legacy standalone plugin binaries are private artifacts and should not be added here.

Refresh these artifacts after rebuilding or redeploying a plugin if the build tool replaces the target file instead of updating it in place.
