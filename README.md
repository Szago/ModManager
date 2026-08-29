# Mod Manager

BepInEx plugin ID: `com.jaqb.eros.modmanager`

Adds a **MOD MANAGER** button below the FPS controls in the game's settings
panel. The button opens a persistent right-side panel listing discovered
BepInEx plugins with restart-state toggles.

## Behavior

- Toggling a mod never unloads or starts it in the current game session.
- Enabled plugin files are staged as `.dll.disabled`; enabling restores the
  original `.dll` name.
- If Windows locks an active DLL, `ModManager.FileApplier.exe` waits for the
  game process to exit and applies the queued rename before the next launch.
- Disabled plugins remain visible through
  `BepInEx/config/ModManager.registry.tsv`, so they can be enabled again.
- Pending filesystem changes are stored in
  `BepInEx/config/ModManager.pending.tsv`.
- The manager excludes its own plugin GUID and DLL. BepInEx core assemblies are
  not plugins and are never included.

The registry and pending files encode text fields as Base64-separated TSV. File
operations are restricted to the configured BepInEx plugins directory and to
`.dll` / `.dll.disabled` paths.

## Build and deploy

From the workspace root:

```powershell
.\tools\Build-Mod.ps1 -Mod ModManager -Configuration Release
```

Deploy both the plugin and its helper with:

```powershell
.\tools\Deploy-Mod.ps1 -Mod ModManager -Configuration Release
```

The deploy target copies `ModManager.dll` and
`ModManager.FileApplier.exe` into `BepInEx/plugins`.

Runtime messages use the `[ModManager]` prefix in
`BepInEx/LogOutput.log`.
