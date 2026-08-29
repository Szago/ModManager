# Mod Manager

BepInEx plugin ID: `com.eros.modmanager`

Adds a **MOD MANAGER** button below the FPS controls in the game's settings
panel. The button opens a game-styled panel listing mods integrated with the
Mod Manager API and their restart-state toggles.

## Behavior

- Toggling a mod never unloads or starts it in the current game session.
- Desired states are ordinary Boolean entries in
  `BepInEx/config/ModManager.states.cfg`.
- On the next launch, an integrated mod calls
  `ModManagerApi.IsEnabled(pluginGuid)` before installing patches, event
  handlers, coroutines, or UI.
- Disabled DLLs remain untouched and loaded by BepInEx, but their plugin
  initialization exits immediately.
- Mods declare a BepInEx dependency on `com.eros.modmanager`, which lets
  the manager list only plugins that actually support this API.
- Mod Manager itself is never listed and cannot be disabled.

There is no helper process, pending queue, DLL renaming, or filesystem mutation.

## Build and deploy

From the workspace root:

```powershell
.\tools\Build-Mod.ps1 -Mod ModManager -Configuration Release
```

Deploy the plugin with:

```powershell
.\tools\Deploy-Mod.ps1 -Mod ModManager -Configuration Release
```

The deploy target copies only `ModManager.dll` into `BepInEx/plugins`.

Runtime messages use the `[ModManager]` prefix in
`BepInEx/LogOutput.log`.
