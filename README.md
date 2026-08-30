# Mod Manager

BepInEx plugin ID: `com.eros.modmanager`

Adds a **MOD MANAGER** button below the FPS controls in the game's settings
panel. The button opens a game-styled panel listing mods integrated with the
Mod Manager API and their restart-state toggles. Mods can also register owned
choice settings; those entries receive a gear button that opens a smaller
game-styled settings popup.

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
- Choice settings remain owned and persisted by the integrating mod through its
  own BepInEx `ConfigEntry<string>`. Mod Manager only renders the registered
  choices and updates that entry.

There is no helper process, pending queue, DLL renaming, or filesystem mutation.

## Registering a choice setting

Bind and register settings before the enabled-state guard so their gear remains
available even when the mod is inactive for the current session. Runtime hooks
and UI still belong after the guard.

```csharp
ConfigEntry<string> preset = Config.Bind(
    "Keybinds", "Preset", "numbers", "Active key preset.");

ModManagerApi.RegisterChoiceSetting(
    PluginGuid,
    "keybind-preset",
    "KEYBIND PRESET",
    preset,
    new ModChoiceOption("numbers", "1 / 2 / 3 / 4 / 5"),
    new ModChoiceOption("letters", "Z / X / C / V / B"));

if (!ModManagerApi.IsEnabled(PluginGuid))
{
    enabled = false;
    return;
}
```

Setting keys and option values must be stable and non-empty. A choice setting
requires at least two uniquely valued options. Assigning a choice updates the
owning config entry immediately.

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
